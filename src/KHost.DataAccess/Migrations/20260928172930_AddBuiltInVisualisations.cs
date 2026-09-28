using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddBuiltInVisualisations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows already there take what a new entry starts with; the model has no schema default.
            migrationBuilder.AddColumn<int>(
                name: "BarCount",
                table: "VisualisationEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 32);

            migrationBuilder.AddColumn<string>(
                name: "Colour",
                table: "VisualisationEntries",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#33ccff");

            migrationBuilder.AddColumn<int>(
                name: "ColourScheme",
                table: "VisualisationEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BarCount",
                table: "VisualisationEntries");

            migrationBuilder.DropColumn(
                name: "Colour",
                table: "VisualisationEntries");

            migrationBuilder.DropColumn(
                name: "ColourScheme",
                table: "VisualisationEntries");
        }
    }
}
