using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelCategories",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    BannerUrl = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SubscriberCount = table.Column<int>(type: "INTEGER", nullable: true),
                    VideoCount = table.Column<int>(type: "INTEGER", nullable: true),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsAutoSyncEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPlaylists",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlaylists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    VideosTabViewMode = table.Column<byte>(type: "INTEGER", nullable: false),
                    AvailableTabViewMode = table.Column<byte>(type: "INTEGER", nullable: false),
                    EnableChannelCategories = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationRoleApplicationUser",
                columns: table => new
                {
                    RolesId = table.Column<string>(type: "TEXT", nullable: false),
                    UsersId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationRoleApplicationUser", x => new { x.RolesId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_ApplicationRoleApplicationUser_AspNetRoles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationRoleApplicationUser_AspNetUsers_UsersId",
                        column: x => x.UsersId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdditionalContent",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdditionalContent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdditionalContent_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Playlists",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VideoCount = table.Column<int>(type: "INTEGER", nullable: true),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Playlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Playlists_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserChannels",
                schema: "app",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserChannels", x => new { x.UserId, x.ChannelId });
                    table.ForeignKey(
                        name: "FK_UserChannels_ChannelCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "app",
                        principalTable: "ChannelCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserChannels_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Videos",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VideoId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    UploadDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LikeCount = table.Column<int>(type: "INTEGER", nullable: true),
                    DownloadedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsQueued = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsManuallyImported = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    NeedsMetadataReview = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    DownloadFailed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Videos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Videos_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelTags",
                schema: "app",
                columns: table => new
                {
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelTags", x => new { x.ChannelId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ChannelTags_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChannelTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomPlaylistTags",
                schema: "app",
                columns: table => new
                {
                    CustomPlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlaylistTags", x => new { x.CustomPlaylistId, x.TagId });
                    table.ForeignKey(
                        name: "FK_CustomPlaylistTags_CustomPlaylists_CustomPlaylistId",
                        column: x => x.CustomPlaylistId,
                        principalSchema: "app",
                        principalTable: "CustomPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomPlaylistTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlaylistAdditionalContent",
                schema: "app",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalContentItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistAdditionalContent", x => new { x.PlaylistId, x.AdditionalContentItemId });
                    table.ForeignKey(
                        name: "FK_PlaylistAdditionalContent_AdditionalContent_AdditionalContentItemId",
                        column: x => x.AdditionalContentItemId,
                        principalSchema: "app",
                        principalTable: "AdditionalContent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistAdditionalContent_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistTags",
                schema: "app",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistTags", x => new { x.PlaylistId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PlaylistTags_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaylistTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPlaylists",
                schema: "app",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UseCustomOrder = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaylists", x => new { x.UserId, x.PlaylistId });
                    table.ForeignKey(
                        name: "FK_UserPlaylists_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SeriesPlaylists",
                schema: "app",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesPlaylists", x => new { x.SeriesId, x.PlaylistId });
                    table.ForeignKey(
                        name: "FK_SeriesPlaylists_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesPlaylists_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalSchema: "app",
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomPlaylistVideos",
                schema: "app",
                columns: table => new
                {
                    CustomPlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlaylistVideos", x => new { x.CustomPlaylistId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_CustomPlaylistVideos_CustomPlaylists_CustomPlaylistId",
                        column: x => x.CustomPlaylistId,
                        principalSchema: "app",
                        principalTable: "CustomPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomPlaylistVideos_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlaylistVideos",
                schema: "app",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistVideos", x => new { x.PlaylistId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_PlaylistVideos_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaylistVideos_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserPlaylistVideos",
                schema: "app",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaylistVideos", x => new { x.UserId, x.PlaylistId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_UserPlaylistVideos_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserPlaylistVideos_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserVideos",
                schema: "app",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Watched = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsIgnored = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVideos", x => new { x.UserId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_UserVideos_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VideoAdditionalContent",
                schema: "app",
                columns: table => new
                {
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalContentItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAdditionalContent", x => new { x.VideoId, x.AdditionalContentItemId });
                    table.ForeignKey(
                        name: "FK_VideoAdditionalContent_AdditionalContent_AdditionalContentItemId",
                        column: x => x.AdditionalContentItemId,
                        principalSchema: "app",
                        principalTable: "AdditionalContent",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VideoAdditionalContent_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoTags",
                schema: "app",
                columns: table => new
                {
                    VideoId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoTags", x => new { x.VideoId, x.TagId });
                    table.ForeignKey(
                        name: "FK_VideoTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoTags_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContent_ChannelId",
                schema: "app",
                table: "AdditionalContent",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationRoleApplicationUser_UsersId",
                table: "ApplicationRoleApplicationUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_Platform_ChannelId",
                schema: "app",
                table: "Channels",
                columns: new[] { "Platform", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelTags_TagId",
                schema: "app",
                table: "ChannelTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPlaylistTags_TagId",
                schema: "app",
                table: "CustomPlaylistTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPlaylistVideos_VideoId",
                schema: "app",
                table: "CustomPlaylistVideos",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistAdditionalContent_AdditionalContentItemId",
                schema: "app",
                table: "PlaylistAdditionalContent",
                column: "AdditionalContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_ChannelId",
                schema: "app",
                table: "Playlists",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Playlists_Platform_PlaylistId",
                schema: "app",
                table: "Playlists",
                columns: new[] { "Platform", "PlaylistId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistTags_TagId",
                schema: "app",
                table: "PlaylistTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistVideos_VideoId",
                schema: "app",
                table: "PlaylistVideos",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_ChannelId",
                schema: "app",
                table: "Series",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesPlaylists_PlaylistId",
                schema: "app",
                table: "SeriesPlaylists",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChannels_CategoryId",
                schema: "app",
                table: "UserChannels",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserChannels_ChannelId",
                schema: "app",
                table: "UserChannels",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaylists_PlaylistId",
                schema: "app",
                table: "UserPlaylists",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaylistVideos_PlaylistId",
                schema: "app",
                table: "UserPlaylistVideos",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlaylistVideos_VideoId",
                schema: "app",
                table: "UserPlaylistVideos",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVideos_VideoId",
                schema: "app",
                table: "UserVideos",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAdditionalContent_AdditionalContentItemId",
                schema: "app",
                table: "VideoAdditionalContent",
                column: "AdditionalContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_ChannelId",
                schema: "app",
                table: "Videos",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Videos_Platform_VideoId",
                schema: "app",
                table: "Videos",
                columns: new[] { "Platform", "VideoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoTags_TagId",
                schema: "app",
                table: "VideoTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationRoleApplicationUser");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "ChannelTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CustomPlaylistTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CustomPlaylistVideos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PlaylistAdditionalContent",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PlaylistTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PlaylistVideos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "SeriesPlaylists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserChannels",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserPlaylists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserPlaylistVideos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserSettings",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserVideos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "VideoAdditionalContent",
                schema: "app");

            migrationBuilder.DropTable(
                name: "VideoTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "CustomPlaylists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Series",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ChannelCategories",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Playlists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "AdditionalContent",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Tags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Videos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Channels",
                schema: "app");
        }
    }
}
