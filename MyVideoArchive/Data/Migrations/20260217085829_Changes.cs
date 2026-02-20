using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class Changes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Videos_Playlists_PlaylistId",
            table: "Videos");

        migrationBuilder.DropIndex(
            name: "IX_Videos_PlaylistId",
            table: "Videos");

        migrationBuilder.DropColumn(
            name: "PlaylistId",
            table: "Videos");

        migrationBuilder.AddColumn<bool>(
            name: "IsQueued",
            table: "Videos",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsIgnored",
            table: "Playlists",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "VideoPlaylists",
            columns: table => new
            {
                VideoId = table.Column<int>(type: "int", nullable: false),
                PlaylistId = table.Column<int>(type: "int", nullable: false)
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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "VideoPlaylists");

        migrationBuilder.DropColumn(
            name: "IsQueued",
            table: "Videos");

        migrationBuilder.DropColumn(
            name: "IsIgnored",
            table: "Playlists");

        migrationBuilder.AddColumn<int>(
            name: "PlaylistId",
            table: "Videos",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Videos_PlaylistId",
            table: "Videos",
            column: "PlaylistId");

        migrationBuilder.AddForeignKey(
            name: "FK_Videos_Playlists_PlaylistId",
            table: "Videos",
            column: "PlaylistId",
            principalTable: "Playlists",
            principalColumn: "Id");
    }
}
