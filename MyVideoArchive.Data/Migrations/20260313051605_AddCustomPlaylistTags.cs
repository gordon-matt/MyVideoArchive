using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomPlaylistTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomPlaylistTags",
                schema: "app",
                columns: table => new
                {
                    CustomPlaylistId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false),
                    CustomPlaylistId1 = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlaylistTags", x => new { x.CustomPlaylistId, x.TagId });
                    table.ForeignKey(
                        name: "FK_CustomPlaylistTags_CustomPlaylists_CustomPlaylistId",
                        column: x => x.CustomPlaylistId,
                        principalSchema: "app",
                        principalTable: "CustomPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomPlaylistTags_CustomPlaylists_CustomPlaylistId1",
                        column: x => x.CustomPlaylistId1,
                        principalSchema: "app",
                        principalTable: "CustomPlaylists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CustomPlaylistTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomPlaylistTags_CustomPlaylistId1",
                schema: "app",
                table: "CustomPlaylistTags",
                column: "CustomPlaylistId1");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPlaylistTags_TagId",
                schema: "app",
                table: "CustomPlaylistTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomPlaylistTags",
                schema: "app");
        }
    }
}