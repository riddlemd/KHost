using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written: EF scaffolds neither the FTS5 table nor its triggers, so re-author this
            // whenever the schema is regenerated or search throws "no such table: media_fts".
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE IF NOT EXISTS "media_fts" USING fts5(
                    media_id UNINDEXED,
                    text,
                    tokenize = 'trigram'
                );
                """);

            // One folded column rather than title and artist apart: the index has to hold exactly
            // the text the short-query fallback searches, or the two paths disagree about what a
            // song is findable by. Folding is what makes an accent or a script findable by its
            // plain spelling, and bundled SQLite cannot do it in SQL.
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ai" AFTER INSERT ON "Media" BEGIN
                    INSERT INTO "media_fts"("media_id", "text")
                    VALUES (new."Id", new."SearchFolded");
                END;
                """);

            // On SearchFolded, not Title/Artist: the model refolds it whenever either changes, and
            // the startup refold repair writes it directly, so this catches both.
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE OF "SearchFolded" ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "text")
                    VALUES (new."Id", new."SearchFolded");
                END;
                """);

            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ad" AFTER DELETE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                END;
                """);

            // Rebuilt rather than appended, so running this against a database that already has an
            // index does not double-insert every row. Writes to media_fts directly, so the triggers
            // above do not fire here.
            migrationBuilder.Sql("""DELETE FROM "media_fts";""");
            migrationBuilder.Sql("""
                INSERT INTO "media_fts"("media_id", "text")
                SELECT "Id", "SearchFolded" FROM "Media";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "media_fts";""");
        }
    }
}
