using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyVideoArchive.Data.Npgsql.Migrations
{
    /// <inheritdoc />
    public partial class AddSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Series",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChannelId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "app",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesPlaylists",
                schema: "app",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "integer", nullable: false),
                    PlaylistId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesPlaylists", x => new { x.SeriesId, x.PlaylistId });
                    table.ForeignKey(
                        name: "FK_SeriesPlaylists_Playlists_PlaylistId",
                        column: x => x.PlaylistId,
                        principalSchema: "app",
                        principalTable: "Playlists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesPlaylists_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalSchema: "app",
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Series_ChannelId",
                schema: "app",
                table: "Series",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesPlaylists_PlaylistId",
                schema: "app",
                table: "SeriesPlaylists",
                column: "PlaylistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeriesPlaylists",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Series",
                schema: "app");
        }
    }
}
