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
                CREATE TRIGGER "media_au" AFTER UPDATE ON "Media" BEGIN
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
