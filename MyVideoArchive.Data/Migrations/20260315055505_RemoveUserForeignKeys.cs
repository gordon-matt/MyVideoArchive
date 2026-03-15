using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserChannels_AspNetUsers_UserId",
                schema: "app",
                table: "UserChannels");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPlaylists_AspNetUsers_UserId",
                schema: "app",
                table: "UserPlaylists");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPlaylistVideos_AspNetUsers_UserId",
                schema: "app",
                table: "UserPlaylistVideos");

            migrationBuilder.DropForeignKey(
                name: "FK_UserSettings_AspNetUsers_UserId",
                schema: "app",
                table: "UserSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_UserVideos_AspNetUsers_UserId",
                schema: "app",
                table: "UserVideos");

            migrationBuilder.DropIndex(
                name: "IX_UserSettings_UserId",
                schema: "app",
                table: "UserSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId",
                schema: "app",
                table: "UserSettings",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserChannels_AspNetUsers_UserId",
                schema: "app",
                table: "UserChannels",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPlaylists_AspNetUsers_UserId",
                schema: "app",
                table: "UserPlaylists",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPlaylistVideos_AspNetUsers_UserId",
                schema: "app",
                table: "UserPlaylistVideos",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserSettings_AspNetUsers_UserId",
                schema: "app",
                table: "UserSettings",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserVideos_AspNetUsers_UserId",
                schema: "app",
                table: "UserVideos",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }
    }
}