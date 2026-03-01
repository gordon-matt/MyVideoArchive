using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCustomPlaylistsAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomPlaylists",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlaylists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomPlaylistVideos",
                schema: "app",
                columns: table => new
                {
                    CustomPlaylistId = table.Column<int>(type: "integer", nullable: false),
                    VideoId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPlaylistVideos", x => new { x.CustomPlaylistId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_CustomPlaylistVideos_CustomPlaylists_CustomPlaylistId",
                        column: x => x.CustomPlaylistId,
                        principalSchema: "app",
                        principalTable: "CustomPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomPlaylistVideos_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VideoTags",
                schema: "app",
                columns: table => new
                {
                    VideoId = table.Column<int>(type: "integer", nullable: false),
                    TagId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoTags", x => new { x.VideoId, x.TagId });
                    table.ForeignKey(
                        name: "FK_VideoTags_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "app",
                        principalTable: "Tags",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VideoTags_Videos_VideoId",
                        column: x => x.VideoId,
                        principalSchema: "app",
                        principalTable: "Videos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomPlaylistVideos_VideoId",
                schema: "app",
                table: "CustomPlaylistVideos",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoTags_TagId",
                schema: "app",
                table: "VideoTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomPlaylistVideos",
                schema: "app");

            migrationBuilder.DropTable(
                name: "VideoTags",
                schema: "app");

            migrationBuilder.DropTable(
                name: "CustomPlaylists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Tags",
                schema: "app");
        }
    }
}
