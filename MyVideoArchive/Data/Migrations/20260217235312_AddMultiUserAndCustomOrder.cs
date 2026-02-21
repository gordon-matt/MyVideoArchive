using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class AddMultiUserAndCustomOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CustomOrder",
            table: "UserPlaylists");

        migrationBuilder.CreateTable(
            name: "UserVideoOrders",
            columns: table => new
            {
                UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                PlaylistId = table.Column<int>(type: "int", nullable: false),
                VideoId = table.Column<int>(type: "int", nullable: false),
                CustomOrder = table.Column<int>(type: "int", nullable: false)
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
                    principalTable: "Playlists",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserVideoOrders_Videos_VideoId",
                    column: x => x.VideoId,
                    principalTable: "Videos",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserVideoOrders_PlaylistId",
            table: "UserVideoOrders",
            column: "PlaylistId");

        migrationBuilder.CreateIndex(
            name: "IX_UserVideoOrders_VideoId",
            table: "UserVideoOrders",
            column: "VideoId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UserVideoOrders");

        migrationBuilder.AddColumn<int>(
            name: "CustomOrder",
            table: "UserPlaylists",
            type: "int",
            nullable: true);
    }
}