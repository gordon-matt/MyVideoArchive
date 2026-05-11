using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class RefactorAdditionalContentJunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdditionalContent_Playlists_PlaylistId",
                schema: "app",
                table: "AdditionalContent");

            migrationBuilder.DropForeignKey(
                name: "FK_AdditionalContent_Videos_VideoId",
                schema: "app",
                table: "AdditionalContent");

            migrationBuilder.DropIndex(
                name: "IX_AdditionalContent_PlaylistId",
                schema: "app",
                table: "AdditionalContent");

            migrationBuilder.DropIndex(
                name: "IX_AdditionalContent_VideoId",
                schema: "app",
                table: "AdditionalContent");

            migrationBuilder.DropColumn(
                name: "PlaylistId",
                schema: "app",
                table: "AdditionalContent");

            migrationBuilder.DropColumn(
                name: "VideoId",
                schema: "app",
                table: "AdditionalContent");

            migrationBuilder.CreateTable(
                name: "PlaylistAdditionalContent",
                schema: "app",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "integer", nullable: false),
                    AdditionalContentItemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistAdditionalContent", x => new { x.PlaylistId, x.AdditionalContentItemId });
                    table.ForeignKey(
                        name: "FK_PlaylistAdditionalContent_AdditionalContent_AdditionalConte~",
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
                name: "VideoAdditionalContent",
                schema: "app",
                columns: table => new
                {
                    VideoId = table.Column<int>(type: "integer", nullable: false),
                    AdditionalContentItemId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAdditionalContent", x => new { x.VideoId, x.AdditionalContentItemId });
                    table.ForeignKey(
                        name: "FK_VideoAdditionalContent_AdditionalContent_AdditionalContentI~",
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

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistAdditionalContent_AdditionalContentItemId",
                schema: "app",
                table: "PlaylistAdditionalContent",
                column: "AdditionalContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoAdditionalContent_AdditionalContentItemId",
                schema: "app",
                table: "VideoAdditionalContent",
                column: "AdditionalContentItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistAdditionalContent",
                schema: "app");

            migrationBuilder.DropTable(
                name: "VideoAdditionalContent",
                schema: "app");

            migrationBuilder.AddColumn<int>(
                name: "PlaylistId",
                schema: "app",
                table: "AdditionalContent",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoId",
                schema: "app",
                table: "AdditionalContent",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContent_PlaylistId",
                schema: "app",
                table: "AdditionalContent",
                column: "PlaylistId");

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalContent_VideoId",
                schema: "app",
                table: "AdditionalContent",
                column: "VideoId");

            migrationBuilder.AddForeignKey(
                name: "FK_AdditionalContent_Playlists_PlaylistId",
                schema: "app",
                table: "AdditionalContent",
                column: "PlaylistId",
                principalSchema: "app",
                principalTable: "Playlists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AdditionalContent_Videos_VideoId",
                schema: "app",
                table: "AdditionalContent",
                column: "VideoId",
                principalSchema: "app",
                principalTable: "Videos",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
