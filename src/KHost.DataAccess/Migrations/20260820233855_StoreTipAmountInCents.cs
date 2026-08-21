using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <summary>
    /// Tip amounts become whole cents in an INTEGER column. SQLite has no decimal type, so the old
    /// decimal was stored as TEXT: exact to read back, but lexicographic, so "100.00" sorted before
    /// "9.00" and the SUM behind a singer's tip total went through floating point to get a number.
    /// </summary>
    public partial class StoreTipAmountInCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolding this as drop-then-add would discard every recorded amount, so the values
            // are carried across before the old column goes.
            migrationBuilder.AddColumn<int>(
                name: "AmountInCents",
                table: "Tips",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // ROUND before CAST: multiplying by 100 in floating point leaves 0.29 as 28.999...,
            // and an integer CAST truncates rather than rounds.
            migrationBuilder.Sql("""
                UPDATE "Tips"
                SET "AmountInCents" = CAST(ROUND(CAST("Amount" AS REAL) * 100) AS INTEGER);
                """);

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "Tips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "Tips",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // printf, not a plain cast: the text form has to keep both decimal places.
            migrationBuilder.Sql("""
                UPDATE "Tips"
                SET "Amount" = printf('%.2f', "AmountInCents" / 100.0);
                """);

            migrationBuilder.DropColumn(
                name: "AmountInCents",
                table: "Tips");
        }
    }
}
