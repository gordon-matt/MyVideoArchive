using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Migrations;

/// <inheritdoc />
public partial class Channel_Images : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "ThumbnailUrl",
            schema: "app",
            table: "Channels",
            newName: "BannerUrl");

        migrationBuilder.AddColumn<string>(
            name: "AvatarUrl",
            schema: "app",
            table: "Channels",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AvatarUrl",
            schema: "app",
            table: "Channels");

        migrationBuilder.RenameColumn(
            name: "BannerUrl",
            schema: "app",
            table: "Channels",
            newName: "ThumbnailUrl");
    }
}