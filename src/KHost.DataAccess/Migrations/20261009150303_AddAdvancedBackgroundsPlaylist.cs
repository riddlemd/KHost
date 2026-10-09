using System;
using KHost.DataAccess.Services;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedBackgroundsPlaylist : Migration
    {
        // Data only, like AddDefaultVisualisationPlaylist. The names are copied rather than read from
        // ShippedVisualisationPlaylists so a later edit there cannot change what this step did.
        private static readonly Guid AdvancedId = ShippedVisualisationPlaylists.AdvancedId;

        private static readonly string[] AdvancedPresetNames =
        [
            "ambient-clouds",
            "ambient-nebula",
            "ambient-aurora",
            "ambient-smoke",
            "ambient-ink",
            "ambient-gasgiant",
            "ambient-haze",
            "ambient-storm",
            "ambient-silk",
        ];

        // A host who renamed the shipped playlist keeps their name.
        private const string RenameBasic =
            """UPDATE "VisualisationPlaylists" SET "Name" = 'Basic Backgrounds', "NameFolded" = 'basic backgrounds' """ +
            """WHERE "Id" = '00000000-0000-0000-0000-000000000001' AND "Name" = 'Default Visualizations'""";

        private const string RenameBasicBack =
            """UPDATE "VisualisationPlaylists" SET "Name" = 'Default Visualizations', "NameFolded" = 'default visualizations' """ +
            """WHERE "Id" = '00000000-0000-0000-0000-000000000001' AND "Name" = 'Basic Backgrounds'""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RenameBasic);

            migrationBuilder.InsertData(
                table: "VisualisationPlaylists",
                columns: ["Id", "Name", "NameFolded", "Shuffle"],
                values: [AdvancedId, "Advanced Backgrounds", "advanced backgrounds", false]);

            for (var i = 0; i < AdvancedPresetNames.Length; i++)
            {
                migrationBuilder.InsertData(
                    table: "VisualisationEntries",
                    columns:
                    [
                        "Id", "VisualisationPlaylistId", "Position", "PresetSource", "PresetName",
                        "Brightness", "Saturation", "Sensitivity", "BarCount", "Colour", "ColourScheme"
                    ],
                    values:
                    [
                        new Guid($"20000000-0000-0000-0000-{i + 1:D12}"), AdvancedId, i, /* VisualiserPresetSource.BuiltIn */ 2,
                        AdvancedPresetNames[i], 100, 100, 100, 32, "#33ccff", /* VisualiserColourScheme.Classic */ 0
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
                keyValue: AdvancedId);

            migrationBuilder.Sql(RenameBasicBack);
        }
    }
}
