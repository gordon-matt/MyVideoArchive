using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyVideoArchive.Data.Migrations;

/// <inheritdoc />
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Channels",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ChannelId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                ThumbnailUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastChecked = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Channels", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Playlists",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PlaylistId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                LastChecked = table.Column<DateTime>(type: "datetime2", nullable: true),
                ChannelId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Playlists", x => x.Id);
                table.ForeignKey(
                    name: "FK_Playlists_Channels_ChannelId",
                    column: x => x.ChannelId,
                    principalTable: "Channels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Videos",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                VideoId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                ThumbnailUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Duration = table.Column<TimeSpan>(type: "time", nullable: true),
                UploadDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                DownloadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                FileSize = table.Column<long>(type: "bigint", nullable: true),
                ChannelId = table.Column<int>(type: "int", nullable: false),
                PlaylistId = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Videos", x => x.Id);
                table.ForeignKey(
                    name: "FK_Videos_Channels_ChannelId",
                    column: x => x.ChannelId,
                    principalTable: "Channels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Videos_Playlists_PlaylistId",
                    column: x => x.PlaylistId,
                    principalTable: "Playlists",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Channels_ChannelId",
            table: "Channels",
            column: "ChannelId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Playlists_ChannelId",
            table: "Playlists",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_Playlists_PlaylistId",
            table: "Playlists",
            column: "PlaylistId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Videos_ChannelId",
            table: "Videos",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_Videos_PlaylistId",
            table: "Videos",
            column: "PlaylistId");

        migrationBuilder.CreateIndex(
            name: "IX_Videos_VideoId",
            table: "Videos",
            column: "VideoId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Videos");

        migrationBuilder.DropTable(
            name: "Playlists");

        migrationBuilder.DropTable(
            name: "Channels");
    }
}
