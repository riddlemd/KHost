using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class BackfillQrCodeEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only: Settings is a JSON column, so a new property needs no schema change. It
            // does need this, though — EF reads a key missing from a stored row as default, which
            // for a bool is false, and ignores the property initializer that says otherwise. Every
            // venue saved before QR codes existed would therefore have them switched off, with
            // nothing on screen to explain why a plugin's code never appears.
            migrationBuilder.Sql("""
                UPDATE "Venues"
                SET "Settings" = json_set("Settings", '$.QrCodeEnabled', json('true'))
                WHERE "Settings" IS NOT NULL
                  AND json_valid("Settings")
                  AND json_extract("Settings", '$.QrCodeEnabled') IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Takes the key back out rather than writing false: absent is what a venue looked like
            // before this ran, and false is a choice nobody made.
            migrationBuilder.Sql("""
                UPDATE "Venues"
                SET "Settings" = json_remove("Settings", '$.QrCodeEnabled')
                WHERE "Settings" IS NOT NULL
                  AND json_valid("Settings");
                """);
        }
    }
}
