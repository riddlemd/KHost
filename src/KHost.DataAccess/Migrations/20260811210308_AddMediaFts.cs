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
            // Hand-written: EF scaffolds neither the FTS5 virtual table nor its triggers, so this
            // body has to be re-authored whenever the schema is regenerated. Without it, search
            // throws "no such table: media_fts".
            // Idempotent, because an earlier (since-deleted) AddMediaFts migration may have
            // already created these objects on existing databases.
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE IF NOT EXISTS "media_fts" USING fts5(
                    media_id UNINDEXED,
                    title,
                    artist,
                    notes,
                    tokenize = 'trigram'
                );
                """);

            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ai" AFTER INSERT ON "Media" BEGIN
                    INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                    VALUES (new."Id", new."Title", new."Artist", COALESCE(new."Notes", ''));
                END;
                """);

            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                    VALUES (new."Id", new."Title", new."Artist", COALESCE(new."Notes", ''));
                END;
                """);

            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ad" AFTER DELETE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                END;
                """);

            // Rebuild the index from scratch so existing rows are covered without
            // double-inserting when media_fts was already populated. Writes directly to
            // media_fts (not Media), so the triggers above do not fire here.
            migrationBuilder.Sql("""DELETE FROM "media_fts";""");
            migrationBuilder.Sql("""
                INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                SELECT "Id", "Title", "Artist", COALESCE("Notes", '') FROM "Media";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "media_fts";""");
        }
    }
}
