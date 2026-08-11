using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <summary>
    /// Data-only: Venue.Settings is a JSON column, so a new setting adds no schema. EF reads a
    /// missing key as default(bool) rather than the property initializer, so venues saved before
    /// TippingEnabled existed would come back false and lose tipping without being asked.
    /// </summary>
    public partial class BackfillTippingEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Venues
                SET Settings = json_set(Settings, '$.TippingEnabled', json('true'))
                WHERE json_extract(Settings, '$.TippingEnabled') IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Venues
                SET Settings = json_remove(Settings, '$.TippingEnabled');
                """);
        }
    }
}
