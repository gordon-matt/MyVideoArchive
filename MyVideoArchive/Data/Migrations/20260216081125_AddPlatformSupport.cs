using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class AddPlatformSupport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Videos_VideoId",
            table: "Videos");

        migrationBuilder.DropIndex(
            name: "IX_Playlists_PlaylistId",
            table: "Playlists");

        migrationBuilder.DropIndex(
            name: "IX_Channels_ChannelId",
            table: "Channels");

        migrationBuilder.AddColumn<int>(
            name: "LikeCount",
            table: "Videos",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Platform",
            table: "Videos",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "ViewCount",
            table: "Videos",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Platform",
            table: "Playlists",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "VideoCount",
            table: "Playlists",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Platform",
            table: "Channels",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "SubscriberCount",
            table: "Channels",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "VideoCount",
            table: "Channels",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Videos_Platform_VideoId",
            table: "Videos",
            columns: new[] { "Platform", "VideoId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Playlists_Platform_PlaylistId",
            table: "Playlists",
            columns: new[] { "Platform", "PlaylistId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Channels_Platform_ChannelId",
            table: "Channels",
            columns: new[] { "Platform", "ChannelId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Videos_Platform_VideoId",
            table: "Videos");

        migrationBuilder.DropIndex(
            name: "IX_Playlists_Platform_PlaylistId",
            table: "Playlists");

        migrationBuilder.DropIndex(
            name: "IX_Channels_Platform_ChannelId",
            table: "Channels");

        migrationBuilder.DropColumn(
            name: "LikeCount",
            table: "Videos");

        migrationBuilder.DropColumn(
            name: "Platform",
            table: "Videos");

        migrationBuilder.DropColumn(
            name: "ViewCount",
            table: "Videos");

        migrationBuilder.DropColumn(
            name: "Platform",
            table: "Playlists");

        migrationBuilder.DropColumn(
            name: "VideoCount",
            table: "Playlists");

        migrationBuilder.DropColumn(
            name: "Platform",
            table: "Channels");

        migrationBuilder.DropColumn(
            name: "SubscriberCount",
            table: "Channels");

        migrationBuilder.DropColumn(
            name: "VideoCount",
            table: "Channels");

        migrationBuilder.CreateIndex(
            name: "IX_Videos_VideoId",
            table: "Videos",
            column: "VideoId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Playlists_PlaylistId",
            table: "Playlists",
            column: "PlaylistId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Channels_ChannelId",
            table: "Channels",
            column: "ChannelId",
            unique: true);
    }
}
