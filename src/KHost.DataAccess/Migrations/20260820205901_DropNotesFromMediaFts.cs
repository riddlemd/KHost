using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <summary>
    /// Notes describe media the host has already found — they are not something to look a song up
    /// by. Indexing them meant a word buried in a note pulled up a song whose title and artist had
    /// nothing to do with the query.
    /// </summary>
    public partial class DropNotesFromMediaFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The column list of an FTS5 table cannot be altered, so the table is rebuilt.
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "media_fts";""");

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE "media_fts" USING fts5(
                    media_id UNINDEXED,
                    title,
                    artist,
                    tokenize = 'trigram'
                );
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ai" AFTER INSERT ON "Media" BEGIN
                    INSERT INTO "media_fts"("media_id", "title", "artist")
                    VALUES (new."Id", new."Title", new."Artist");
                END;
                """);

            // Narrowed with the column list: Notes no longer affects the index, so a note edit
            // should not re-index the row.
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE OF "Title", "Artist" ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "title", "artist")
                    VALUES (new."Id", new."Title", new."Artist");
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ad" AFTER DELETE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                END;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "media_fts"("media_id", "title", "artist")
                SELECT "Id", "Title", "Artist" FROM "Media";
                """);

            // The short-query fallback searches the same text the index does, or the two paths
            // would disagree about what a song is findable by.
            migrationBuilder.Sql("""
                UPDATE "Media" SET "SearchFolded" = lower("Title" || ' ' || "Artist");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "media_fts";""");

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE "media_fts" USING fts5(
                    media_id UNINDEXED,
                    title,
                    artist,
                    notes,
                    tokenize = 'trigram'
                );
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ai" AFTER INSERT ON "Media" BEGIN
                    INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                    VALUES (new."Id", new."Title", new."Artist", COALESCE(new."Notes", ''));
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE OF "Title", "Artist", "Notes" ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                    VALUES (new."Id", new."Title", new."Artist", COALESCE(new."Notes", ''));
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ad" AFTER DELETE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                END;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "media_fts"("media_id", "title", "artist", "notes")
                SELECT "Id", "Title", "Artist", COALESCE("Notes", '') FROM "Media";
                """);

            migrationBuilder.Sql("""
                UPDATE "Media"
                SET "SearchFolded" = lower("Title" || ' ' || "Artist" || ' ' || COALESCE("Notes", ''));
                """);
        }
    }
}
