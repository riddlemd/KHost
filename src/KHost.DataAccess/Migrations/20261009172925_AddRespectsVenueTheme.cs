using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddRespectsVenueTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RespectsVenueTheme",
                table: "VisualisationEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Off for a host's own entries, which keep the colours they were given; on for the
            // two shipped playlists, which have no colours of their own to keep.
            migrationBuilder.Sql(
                """UPDATE "VisualisationEntries" SET "RespectsVenueTheme" = 1 """ +
                """WHERE "VisualisationPlaylistId" IN ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002')""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RespectsVenueTheme",
                table: "VisualisationEntries");
        }
    }
}
