using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class FixCustomPlaylistTagForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomPlaylistTags_CustomPlaylists_CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags");

            migrationBuilder.DropIndex(
                name: "IX_CustomPlaylistTags_CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags");

            migrationBuilder.DropColumn(
                name: "CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomPlaylistTags_CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags",
                column: "CustomPlaylistId1");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomPlaylistTags_CustomPlaylists_CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags",
                column: "CustomPlaylistId1",
                principalSchema: "app",
                principalTable: "CustomPlaylists",
                principalColumn: "Id");
        }
    }
}
