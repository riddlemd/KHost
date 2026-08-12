using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTipperUserGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Memberships go first rather than leaning on the join table's ON DELETE CASCADE:
            // SQLite only honours it when foreign_keys is on, which is a connection-level pragma
            // rather than a property of the schema.
            migrationBuilder.Sql("""
                DELETE FROM "UserGroupMemberships"
                WHERE "GroupId" = '00000000-0000-0000-0000-000000000003';
                """);

            migrationBuilder.DeleteData(
                table: "UserGroups",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "UserGroups",
                columns: new[] { "Id", "Description", "Name", "Permissions" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000003"), "Singer who tips the host", "Tipper", "[]" });
        }
    }
}
