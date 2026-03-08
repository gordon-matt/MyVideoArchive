using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Migrations;

/// <inheritdoc />
public partial class Video_AddDownloadFailed : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<bool>(
        name: "DownloadFailed",
        schema: "app",
        table: "Videos",
        type: "boolean",
        nullable: false,
        defaultValue: false);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "DownloadFailed",
        schema: "app",
        table: "Videos");
}