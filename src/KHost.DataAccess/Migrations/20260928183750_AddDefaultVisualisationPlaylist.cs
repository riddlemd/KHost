using System;
using KHost.Abstractions.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultVisualisationPlaylist : Migration
    {
        // Kept out of the model deliberately: a modelBuilder.HasData() here would also seed every
        // EnsureCreated() test database, not only one built by real migrations. Data only, so the
        // schema above needs no change, and DatabaseInitializer.EnsureDefaultVisualisationPlaylistAsync
        // is what puts the row back if it is ever gone from a running install.
        private static readonly Guid PlaylistId = VisualisationPlaylist.DefaultId;

        // Five ambient built-ins, in the order VisualiserPresetService.BuiltIns lists them. A later
        // addition to that list is not retrofitted here — DatabaseInitializer reads it live.
        private static readonly string[] AmbientPresetNames =
        [
            "ambient-gradient",
            "ambient-bokeh",
            "ambient-embers",
            "ambient-rings",
            "ambient-beams",
        ];

        private static readonly Guid[] EntryIds =
        [
            new("10000000-0000-0000-0000-000000000001"),
            new("10000000-0000-0000-0000-000000000002"),
            new("10000000-0000-0000-0000-000000000003"),
            new("10000000-0000-0000-0000-000000000004"),
            new("10000000-0000-0000-0000-000000000005"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "VisualisationPlaylists",
                columns: ["Id", "Name", "NameFolded", "Shuffle"],
                values: [PlaylistId, "Default Visualizations", "default visualizations", false]);

            for (var i = 0; i < AmbientPresetNames.Length; i++)
            {
                migrationBuilder.InsertData(
                    table: "VisualisationEntries",
                    columns:
                    [
                        "Id", "VisualisationPlaylistId", "Position", "PresetSource", "PresetName",
                        "Brightness", "Saturation", "Sensitivity", "DarkenBehindWords", "BarCount", "Colour", "ColourScheme"
                    ],
                    values:
                    [
                        EntryIds[i], PlaylistId, i, /* VisualiserPresetSource.BuiltIn */ 2, AmbientPresetNames[i],
                        100, 100, 100, true, 32, "#33ccff", /* VisualiserColourScheme.Classic */ 0
                    ]);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cascades to the entries: VisualisationEntries.VisualisationPlaylistId is onDelete Cascade.
            migrationBuilder.DeleteData(
                table: "VisualisationPlaylists",
                keyColumn: "Id",
                keyValue: PlaylistId);
        }
    }
}
