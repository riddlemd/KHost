using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddFoldedSearchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameFolded",
                table: "Venues",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NameFolded",
                table: "UserGroups",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NotesFolded",
                table: "Tips",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchFolded",
                table: "Media",
                type: "TEXT",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "UserGroups",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "NameFolded",
                value: "admin");

            migrationBuilder.UpdateData(
                table: "UserGroups",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "NameFolded",
                value: "regular");
            // A starting value only: SQLite's lower() folds ASCII and nothing else, so
            // DatabaseInitializer refolds these properly in .NET on the next start.
            migrationBuilder.Sql("""UPDATE "Venues" SET "NameFolded" = lower("Name");""");
            migrationBuilder.Sql("""UPDATE "UserGroups" SET "NameFolded" = lower("Name");""");
            migrationBuilder.Sql("""UPDATE "Tips" SET "NotesFolded" = lower(COALESCE("Notes", ''));""");
            migrationBuilder.Sql("""
                UPDATE "Media"
                SET "SearchFolded" = lower("Title" || ' ' || "Artist" || ' ' || COALESCE("Notes", ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NameFolded",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "NameFolded",
                table: "UserGroups");

            migrationBuilder.DropColumn(
                name: "NotesFolded",
                table: "Tips");

            migrationBuilder.DropColumn(
                name: "SearchFolded",
                table: "Media");
        }
    }
}
