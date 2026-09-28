using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualisationPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisualisationPlaylists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NameFolded = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Shuffle = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualisationPlaylists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisualisationEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VisualisationPlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    PresetSource = table.Column<int>(type: "INTEGER", nullable: false),
                    PresetName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Brightness = table.Column<int>(type: "INTEGER", nullable: false),
                    Saturation = table.Column<int>(type: "INTEGER", nullable: false),
                    Sensitivity = table.Column<int>(type: "INTEGER", nullable: false),
                    DarkenBehindWords = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualisationEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisualisationEntries_VisualisationPlaylists_VisualisationPlaylistId",
                        column: x => x.VisualisationPlaylistId,
                        principalTable: "VisualisationPlaylists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisualisationEntries_VisualisationPlaylistId",
                table: "VisualisationEntries",
                column: "VisualisationPlaylistId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisualisationEntries");

            migrationBuilder.DropTable(
                name: "VisualisationPlaylists");
        }
    }
}
