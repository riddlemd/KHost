using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaContentFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Media",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "Media",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SampledHash",
                table: "Media",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Media_ContentHash",
                table: "Media",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Media_FileSize",
                table: "Media",
                column: "FileSize");

            // Narrowed to the columns media_fts actually indexes. Import now writes sizes and
            // hashes back to Media, and the unconditional trigger re-indexed the search row for
            // every one of those writes.
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE OF "Title", "Artist", "Notes" ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                    VALUES (new."Id", new."Title", new."Artist", COALESCE(new."Notes", ''));
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                    VALUES (new."Id", new."Title", new."Artist", COALESCE(new."Notes", ''));
                END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Media_ContentHash",
                table: "Media");

            migrationBuilder.DropIndex(
                name: "IX_Media_FileSize",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "SampledHash",
                table: "Media");
        }
    }
}
