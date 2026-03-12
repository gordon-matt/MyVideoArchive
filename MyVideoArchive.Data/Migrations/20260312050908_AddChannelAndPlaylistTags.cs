using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAndPlaylistTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelTags",
                schema: "app",
                columns: table => new
                {
                    ChannelId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelTags", x => new { x.ChannelId, x.TagId });
                    table.ForeignKey(
                        name: "FK_ChannelTags_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChannelTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlaylistTags",
                schema: "app",
                columns: table => new
                {
                    PlaylistId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistTags", x => new { x.PlaylistId, x.TagId });
                    table.ForeignKey(
                        name: "FK_PlaylistTags_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaylistTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelTags_TagId",
                schema: "app",
                table: "ChannelTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistTags_TagId",
                schema: "app",
                table: "PlaylistTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "PlaylistTags",
                schema: "app");
        }
    }
}
