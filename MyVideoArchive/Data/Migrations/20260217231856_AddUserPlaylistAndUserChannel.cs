using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class AddUserPlaylistAndUserChannel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Order",
            table: "VideoPlaylists",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "UserChannels",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                ChannelId = table.Column<int>(type: "int", nullable: false),
                SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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
                    principalTable: "Channels",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "UserPlaylists",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                PlaylistId = table.Column<int>(type: "int", nullable: false),
                SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                CustomOrder = table.Column<int>(type: "int", nullable: true)
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
                    principalTable: "Playlists",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserChannels_ChannelId",
            table: "UserChannels",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_UserPlaylists_PlaylistId",
            table: "UserPlaylists",
            column: "PlaylistId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UserChannels");

        migrationBuilder.DropTable(
            name: "UserPlaylists");

        migrationBuilder.DropColumn(
            name: "Order",
            table: "VideoPlaylists");
    }
}
