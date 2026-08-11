using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <summary>
    /// Data-only: Venue.Settings is a JSON column, so a new setting adds no schema. EF reads a
    /// missing key as default(bool) rather than the property initializer, so venues saved before
    /// ShowEstimatedWaitTime existed would come back false and silently lose the wait time.
    /// </summary>
    public partial class BackfillShowEstimatedWaitTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Venues
                SET Settings = json_set(Settings, '$.ShowEstimatedWaitTime', json('true'))
                WHERE json_extract(Settings, '$.ShowEstimatedWaitTime') IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE Venues
                SET Settings = json_remove(Settings, '$.ShowEstimatedWaitTime');
                """);
        }
    }
}
