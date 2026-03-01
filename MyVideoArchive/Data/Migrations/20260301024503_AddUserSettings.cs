using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserVideoOrders",
                schema: "app");

            migrationBuilder.AddColumn<bool>(
                name: "UseCustomOrder",
                schema: "app",
                table: "UserPlaylists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserPlaylistVideos",
                schema: "app",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PlaylistId = table.Column<int>(type: "integer", nullable: false),
                    VideoId = table.Column<int>(type: "integer", nullable: false),
                    CustomOrder = table.Column<int>(type: "integer", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlaylistVideos", x => new { x.UserId, x.PlaylistId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_UserPlaylistVideos_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
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
                name: "UserSettings",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    VideosTabViewMode = table.Column<byte>(type: "smallint", nullable: false),
                    AvailableTabViewMode = table.Column<byte>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

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
                name: "IX_UserSettings_UserId",
                schema: "app",
                table: "UserSettings",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPlaylistVideos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "UserSettings",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "UseCustomOrder",
                schema: "app",
                table: "UserPlaylists");

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
                name: "IX_UserVideoOrders_PlaylistId",
                schema: "app",
                table: "UserVideoOrders",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVideoOrders_VideoId",
                schema: "app",
                table: "UserVideoOrders",
                column: "VideoId");
        }
    }
}
