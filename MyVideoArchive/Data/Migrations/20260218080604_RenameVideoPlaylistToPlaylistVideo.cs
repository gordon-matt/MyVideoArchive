using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class RenameVideoPlaylistToPlaylistVideo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "VideoPlaylists");

        migrationBuilder.CreateTable(
            name: "PlaylistVideos",
            columns: table => new
            {
                PlaylistId = table.Column<int>(type: "int", nullable: false),
                VideoId = table.Column<int>(type: "int", nullable: false),
                Order = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlaylistVideos", x => new { x.PlaylistId, x.VideoId });
                table.ForeignKey(
                    name: "FK_PlaylistVideos_Playlists_PlaylistId",
                    column: x => x.PlaylistId,
                    principalTable: "Playlists",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_PlaylistVideos_Videos_VideoId",
                    column: x => x.VideoId,
                    principalTable: "Videos",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlaylistVideos_VideoId",
            table: "PlaylistVideos",
            column: "VideoId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PlaylistVideos");

        migrationBuilder.CreateTable(
            name: "VideoPlaylists",
            columns: table => new
            {
                PlaylistId = table.Column<int>(type: "int", nullable: false),
                VideoId = table.Column<int>(type: "int", nullable: false),
                Order = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VideoPlaylists", x => new { x.PlaylistId, x.VideoId });
                table.ForeignKey(
                    name: "FK_VideoPlaylists_Playlists_PlaylistId",
                    column: x => x.PlaylistId,
                    principalTable: "Playlists",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_VideoPlaylists_Videos_VideoId",
                    column: x => x.VideoId,
                    principalTable: "Videos",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_VideoPlaylists_VideoId",
            table: "VideoPlaylists",
            column: "VideoId");
    }
}
