using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <summary>
    /// The full-text index now holds the folded search text instead of the raw title and artist,
    /// so "bjork" finds Björk and "kesha" finds Ke$ha. SQL cannot fold text itself — the triggers
    /// copy Media.SearchFolded, which .NET computes — and queries are folded the same way before
    /// matching, so both sides of the comparison are reduced identically.
    /// </summary>
    public partial class IndexFoldedSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ai";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_au";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "media_ad";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "media_fts";""");

            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE "media_fts" USING fts5(
                    media_id UNINDEXED,
                    text,
                    tokenize = 'trigram'
                );
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ai" AFTER INSERT ON "Media" BEGIN
                    INSERT INTO "media_fts"("media_id", "text")
                    VALUES (new."Id", new."SearchFolded");
                END;
                """);

            // On SearchFolded, not Title/Artist: the model refolds it whenever either changes, and
            // the startup refold repair writes it directly, so this catches both.
            migrationBuilder.Sql("""
                CREATE TRIGGER "media_au" AFTER UPDATE OF "SearchFolded" ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                    INSERT INTO "media_fts"("media_id", "text")
                    VALUES (new."Id", new."SearchFolded");
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER "media_ad" AFTER DELETE ON "Media" BEGIN
                    DELETE FROM "media_fts" WHERE "media_id" = old."Id";
                END;
                """);

            // Seeded from SearchFolded as stored; the folding rule also changed in this release, so
            // the startup refold rewrites SearchFolded in .NET and the trigger re-indexes each row.
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
        }
    }
}
