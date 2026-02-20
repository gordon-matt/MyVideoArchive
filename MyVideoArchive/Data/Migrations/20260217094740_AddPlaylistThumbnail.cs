using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class AddPlaylistThumbnail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "ThumbnailUrl",
        table: "Playlists",
        type: "nvarchar(512)",
        maxLength: 512,
        nullable: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "ThumbnailUrl",
        table: "Playlists");
}
