using System.Net;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MyVideoArchive.Data;

namespace MyVideoArchive.Infrastructure;

internal static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public void MvaAddAuthentication(IConfiguration configuration, bool useKeycloak)
        {
            if (useKeycloak)
            {
                string? keycloakAuthority = configuration["Authentication:Keycloak:Authority"];
                string? keycloakBackchannelBaseUrl = configuration["Authentication:Keycloak:BackchannelBaseUrl"];

                services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.Keycloak, keycloakAuthority));

                // ── Keycloak / OpenID Connect authentication ──────────────────────────────
                // A cookie keeps the server-side session; OIDC handles the external login flow.
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.LoginPath = "/auth/login";
                    options.AccessDeniedPath = "/Home/AccessDenied";
                })
                .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    // Authority: the public Keycloak realm URL the browser uses to reach Keycloak.
                    // The app also uses this for backchannel calls (discovery + token exchange) unless
                    // BackchannelBaseUrl is set, in which case server-to-server calls are routed there.
                    options.Authority = keycloakAuthority;

                    // When BackchannelBaseUrl is set, all backchannel HTTP calls (discovery and the token
                    // exchange) are transparently rerouted to that URL. This avoids hairpin NAT / DNS
                    // issues in Docker where the container cannot reach the public Keycloak hostname.
                    // Example: when app and Keycloak share a Docker network, set BackchannelBaseUrl to
                    // "http://keycloak:8080" so the token exchange goes directly container-to-container.
                    if (!string.IsNullOrWhiteSpace(keycloakBackchannelBaseUrl) && !string.IsNullOrWhiteSpace(keycloakAuthority))
                    {
                        options.BackchannelHttpHandler = new KeycloakBackchannelHandler(keycloakAuthority, keycloakBackchannelBaseUrl);
                    }

                    options.ClientId = configuration["Authentication:Keycloak:ClientId"];
                    options.ClientSecret = configuration["Authentication:Keycloak:ClientSecret"];
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = true;

                    // Map the OIDC "preferred_username" claim to the standard Name claim.
                    // The .NET JWT handler's default inbound claim type map already converts
                    // the "roles" JWT claim → ClaimTypes.Role automatically, so no RoleClaimType
                    // override is needed. [Authorize(Roles = "...")] and IsInRole() both check
                    // ClaimTypes.Role, which is what the handler produces.
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = "preferred_username"
                    };

                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("email");

                    // Required when Keycloak is accessed over HTTP (e.g. local dev or internal Docker).
                    // Remove this once Keycloak is always reached via HTTPS from the app container.
                    options.RequireHttpsMetadata = false;

                    // Keycloak redirects the browser back to /signin-oidc with a POST (cross-site).
                    // Browsers do not send SameSite=Lax cookies on cross-site POSTs, so the OIDC
                    // correlation cookie must be SameSite=None + Secure for the callback to work.
                    // The app must be reached over HTTPS (reverse proxy that sets X-Forwarded-Proto).
                    options.CorrelationCookie.SameSite = SameSiteMode.None;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.NonceCookie.SameSite = SameSiteMode.None;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;

                    // Keycloak 26+ enables Pushed Authorization Requests (PAR) by default.
                    // PAR requires exact redirect URI matches (wildcards are rejected), which
                    // conflicts with the wildcard URIs typically used during development.
                    options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;

                    // When using BackchannelBaseUrl, discovery is fetched from the internal URL so
                    // Configuration.AuthorizationEndpoint has the internal host (e.g. keycloak:8080).
                    // We must send the browser to the public Authority URL instead.
                    string? publicOrigin = configuration["Authentication:Keycloak:PublicOrigin"];
                    bool useBackchannel = !string.IsNullOrWhiteSpace(keycloakBackchannelBaseUrl);
                    if (!string.IsNullOrWhiteSpace(publicOrigin) || useBackchannel)
                    {
                        publicOrigin = publicOrigin?.TrimEnd('/');
                        options.Events.OnRedirectToIdentityProvider = context =>
                        {
                            if (!string.IsNullOrWhiteSpace(publicOrigin))
                                context.ProtocolMessage.RedirectUri = publicOrigin + "/signin-oidc";

                            if (useBackchannel && !string.IsNullOrEmpty(keycloakAuthority))
                            {
                                // Build auth URL from public Authority so the browser goes to
                                // https://keycloak.example.com/... (no internal host/port).
                                string fullUrl = context.ProtocolMessage.CreateAuthenticationRequestUrl();
                                int queryIndex = fullUrl.IndexOf('?');
                                string query = queryIndex >= 0 ? fullUrl.Substring(queryIndex) : string.Empty;
                                string authUrl = keycloakAuthority.TrimEnd('/') + "/protocol/openid-connect/auth" + query;
                                context.Response.Redirect(authUrl);
                                context.HandleResponse();
                            }
                            return Task.CompletedTask;
                        };

                        // Sign-out: post_logout_redirect_uri must match Keycloak's "Valid post logout redirect URIs"
                        // (e.g. https://mva.example.com/signout-callback-oidc). When behind a proxy the app may see
                        // HTTP, so set it explicitly from PublicOrigin.
                        if (!string.IsNullOrWhiteSpace(publicOrigin))
                        {
                            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                            {
                                context.ProtocolMessage.PostLogoutRedirectUri = publicOrigin + "/signout-callback-oidc";
                                return Task.CompletedTask;
                            };
                        }
                    }

                    // Redirect auth failures to the error page rather than re-throwing. Without this
                    // a failed callback redirects to /Home/Error which (if protected) re-challenges,
                    // creating an infinite redirect loop.
                    options.Events.OnRemoteFailure = context =>
                    {
                        context.Response.Redirect("/Home/Error");
                        context.HandleResponse();
                        return Task.CompletedTask;
                    };
                });

                // When behind a reverse proxy (e.g. Synology NAS), trust forwarded headers so the app
                // sees the original HTTPS scheme and host. Required for OIDC redirect URIs and Secure cookies.
                services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                        | ForwardedHeaders.XForwardedProto
                        | ForwardedHeaders.XForwardedHost;
                    options.KnownProxies.Clear();
                    options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
                    options.KnownProxies.Add(IPAddress.Parse("::1"));
                    // Docker bridge: when the app runs in a container, the proxy connects from the host.
                    options.KnownProxies.Add(IPAddress.Parse("172.17.0.1"));
                });

                services.AddScoped<IUserInfoService, KeycloakUserInfoService>();
            }
            else
            {
                services.AddSingleton<IAuthProviderService>(new AuthProviderService(AuthProvider.Identity, null));

                // ── ASP.NET Core Identity (default) ──────────────────────────────────────
                services.AddIdentity<ApplicationUser, ApplicationRole>()
                    .AddEntityFrameworkStores<ApplicationDbContext>()
                    .AddDefaultTokenProviders()
                    .AddDefaultUI();

                services.AddScoped<IUserInfoService, AspNetIdentityUserInfoService>();
            }
        }

        public void MvaAddHangfire(string connectionString)
        {
            services.AddHangfire(configuration => configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    QueuePollInterval = TimeSpan.FromSeconds(15)
                }));

            // Main server: handles all queues except "downloads"
            services.AddHangfireServer(options =>
            {
                options.ServerName = "main";
                options.Queues = ["default", "critical"];
            });

            // Dedicated single-worker server for video downloads — serialises jobs and prevents
            // parallel requests to YouTube that would trigger rate-limiting.
            services.AddHangfireServer(options =>
            {
                options.ServerName = "downloads";
                options.Queues = ["downloads"];
                options.WorkerCount = 1;
            });
        }

        public void MvaAddServices()
        {
            // Register video services
            services.AddSingleton<YoutubeDLInitializer>();
            services.AddSingleton(sp =>
            {
                var initializer = sp.GetRequiredService<YoutubeDLInitializer>();
                return initializer.GetInstanceAsync().GetAwaiter().GetResult();
            });

            // Register metadata providers
            services.AddSingleton<IVideoMetadataProvider, YouTubeMetadataProvider>();
            services.AddSingleton<IVideoMetadataProvider, BitChuteMetadataProvider>();

            // Register downloaders
            services.AddSingleton<IVideoDownloader, YouTubeDownloader>();
            services.AddSingleton<IVideoDownloader, BitChuteDownloader>();

            // Register factories
            services.AddSingleton<VideoMetadataProviderFactory>();
            services.AddSingleton<VideoDownloaderFactory>();

            // Register thumbnail service
            services.AddTransient<ThumbnailService>();

            // Register Hangfire jobs
            services.AddTransient<ChannelSyncJob>();
            services.AddTransient<FileSystemScanJob>();
            services.AddTransient<MetadataReviewJob>();
            services.AddTransient<PlaylistSyncJob>();
            services.AddTransient<VideoDownloadJob>();

            // Register file system scan state (singleton - tracks progress across requests)
            services.AddSingleton<FileSystemScanStateService>();

            // Register user context service
            services.AddHttpContextAccessor();
            services.AddScoped<IUserContextService, UserContextService>();

            // Register other services
            services.AddSingleton<IChannelService, ChannelService>();
            services.AddSingleton<ICustomChannelService, CustomChannelService>();
            services.AddSingleton<ICustomPlaylistService, CustomPlaylistService>();
            services.AddSingleton<IFileSystemScanService, FileSystemScanService>();
            services.AddSingleton<IPlaylistService, PlaylistService>();
            services.AddSingleton<ITagService, TagService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<IVideoService, VideoService>();
        }
    }
}