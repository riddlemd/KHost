using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class BackfillGuestRemoteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only: Settings is a JSON column, so two new properties need no schema change.
            // They do need this, though, because both default on. EF reads a key missing from a
            // stored row as default, which for a bool is false, and ignores the initializer that
            // says otherwise, so every venue that predates these would come back with its guest
            // remote switched off and nothing on screen to explain it.
            migrationBuilder.Sql("""
                UPDATE "Venues"
                SET "Settings" = json_set("Settings", '$.AllowGuestRemote', json('true'))
                WHERE "Settings" IS NOT NULL
                  AND json_valid("Settings")
                  AND json_extract("Settings", '$.AllowGuestRemote') IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE "Venues"
                SET "Settings" = json_set("Settings", '$.ShowQueueToGuests', json('true'))
                WHERE "Settings" IS NOT NULL
                  AND json_valid("Settings")
                  AND json_extract("Settings", '$.ShowQueueToGuests') IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removed rather than set false: absent is what a venue looked like before this ran,
            // and false is a choice nobody made.
            migrationBuilder.Sql("""
                UPDATE "Venues"
                SET "Settings" = json_remove("Settings", '$.AllowGuestRemote', '$.ShowQueueToGuests')
                WHERE "Settings" IS NOT NULL
                  AND json_valid("Settings");
                """);
        }
    }
}
