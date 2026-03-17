using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyVideoArchive.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableChannelCategories",
                schema: "app",
                table: "UserSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                schema: "app",
                table: "UserChannels",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelCategories",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserChannels_CategoryId",
                schema: "app",
                table: "UserChannels",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserChannels_ChannelCategories_CategoryId",
                schema: "app",
                table: "UserChannels",
                column: "CategoryId",
                principalSchema: "app",
                principalTable: "ChannelCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserChannels_ChannelCategories_CategoryId",
                schema: "app",
                table: "UserChannels");

            migrationBuilder.DropTable(
                name: "ChannelCategories",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_UserChannels_CategoryId",
                schema: "app",
                table: "UserChannels");

            migrationBuilder.DropColumn(
                name: "EnableChannelCategories",
                schema: "app",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                schema: "app",
                table: "UserChannels");
        }
    }
}