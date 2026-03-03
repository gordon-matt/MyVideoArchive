using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyVideoArchive.Migrations;

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
                Id = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AspNetRoles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                PasswordHash = table.Column<string>(type: "text", nullable: true),
                SecurityStamp = table.Column<string>(type: "text", nullable: true),
                ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                PhoneNumber = table.Column<string>(type: "text", nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AspNetUsers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Channels",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ChannelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Description = table.Column<string>(type: "text", nullable: true),
                Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SubscriberCount = table.Column<int>(type: "integer", nullable: true),
                VideoCount = table.Column<int>(type: "integer", nullable: true),
                SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Channels", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AspNetRoleClaims",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RoleId = table.Column<string>(type: "text", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true)
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
                RolesId = table.Column<string>(type: "text", nullable: false),
                UsersId = table.Column<string>(type: "text", nullable: false)
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
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                UserId = table.Column<string>(type: "text", nullable: false),
                ClaimType = table.Column<string>(type: "text", nullable: true),
                ClaimValue = table.Column<string>(type: "text", nullable: true)
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
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                ProviderKey = table.Column<string>(type: "text", nullable: false),
                ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                UserId = table.Column<string>(type: "text", nullable: false)
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
                UserId = table.Column<string>(type: "text", nullable: false),
                RoleId = table.Column<string>(type: "text", nullable: false)
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
                UserId = table.Column<string>(type: "text", nullable: false),
                LoginProvider = table.Column<string>(type: "text", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Value = table.Column<string>(type: "text", nullable: true)
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
            name: "Playlists",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PlaylistId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                VideoCount = table.Column<int>(type: "integer", nullable: true),
                SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastChecked = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsIgnored = table.Column<bool>(type: "boolean", nullable: false),
                ChannelId = table.Column<int>(type: "integer", nullable: false)
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
            name: "UserChannels",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "text", nullable: false),
                ChannelId = table.Column<int>(type: "integer", nullable: false),
                SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserChannels", x => new { x.UserId, x.ChannelId });
                table.ForeignKey(
                    name: "FK_UserChannels_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id");
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
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                VideoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                UploadDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ViewCount = table.Column<int>(type: "integer", nullable: true),
                LikeCount = table.Column<int>(type: "integer", nullable: true),
                DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                FileSize = table.Column<long>(type: "bigint", nullable: true),
                IsIgnored = table.Column<bool>(type: "boolean", nullable: false),
                IsQueued = table.Column<bool>(type: "boolean", nullable: false),
                ChannelId = table.Column<int>(type: "integer", nullable: false)
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
            name: "UserPlaylists",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "text", nullable: false),
                PlaylistId = table.Column<int>(type: "integer", nullable: false),
                SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserPlaylists", x => new { x.UserId, x.PlaylistId });
                table.ForeignKey(
                    name: "FK_UserPlaylists_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserPlaylists_Playlists_PlaylistId",
                    column: x => x.PlaylistId,
                    principalSchema: "app",
                    principalTable: "Playlists",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "PlaylistVideos",
            schema: "app",
            columns: table => new
            {
                PlaylistId = table.Column<int>(type: "integer", nullable: false),
                VideoId = table.Column<int>(type: "integer", nullable: false),
                Order = table.Column<int>(type: "integer", nullable: false)
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
            name: "UserVideoOrders",
            schema: "app",
            columns: table => new
            {
                UserId = table.Column<string>(type: "text", nullable: false),
                PlaylistId = table.Column<int>(type: "integer", nullable: false),
                VideoId = table.Column<int>(type: "integer", nullable: false),
                CustomOrder = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserVideoOrders", x => new { x.UserId, x.PlaylistId, x.VideoId });
                table.ForeignKey(
                    name: "FK_UserVideoOrders_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserVideoOrders_Playlists_PlaylistId",
                    column: x => x.PlaylistId,
                    principalSchema: "app",
                    principalTable: "Playlists",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserVideoOrders_Videos_VideoId",
                    column: x => x.VideoId,
                    principalSchema: "app",
                    principalTable: "Videos",
                    principalColumn: "Id");
            });

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
            name: "IX_PlaylistVideos_VideoId",
            schema: "app",
            table: "PlaylistVideos",
            column: "VideoId");

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
            name: "IX_UserVideoOrders_PlaylistId",
            schema: "app",
            table: "UserVideoOrders",
            column: "PlaylistId");

        migrationBuilder.CreateIndex(
            name: "IX_UserVideoOrders_VideoId",
            schema: "app",
            table: "UserVideoOrders",
            column: "VideoId");

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
            name: "PlaylistVideos",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserChannels",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserPlaylists",
            schema: "app");

        migrationBuilder.DropTable(
            name: "UserVideoOrders",
            schema: "app");

        migrationBuilder.DropTable(
            name: "AspNetRoles");

        migrationBuilder.DropTable(
            name: "AspNetUsers");

        migrationBuilder.DropTable(
            name: "Playlists",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Videos",
            schema: "app");

        migrationBuilder.DropTable(
            name: "Channels",
            schema: "app");
    }
}