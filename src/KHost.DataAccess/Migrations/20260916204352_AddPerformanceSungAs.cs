using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceSungAs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SungAs",
                table: "Performances",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            // Every row already here was queued before anything recorded a name, so fill it from
            // the singer each one still points at. Nullable rather than defaulted to "": a row
            // whose singer has already been deleted has no answer, and an empty string would claim
            // it was sung under no name rather than that nobody knows.
            migrationBuilder.Sql(
                @"UPDATE ""Performances""
                  SET ""SungAs"" = (SELECT ""Name"" FROM ""Users"" WHERE ""Users"".""Id"" = ""Performances"".""SingerId"")
                  WHERE ""SungAs"" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SungAs",
                table: "Performances");
        }
    }
}
