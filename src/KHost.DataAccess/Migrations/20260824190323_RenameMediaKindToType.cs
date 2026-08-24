using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RenameMediaKindToType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "Media",
                newName: "Type");

            migrationBuilder.RenameIndex(
                name: "IX_Media_Kind",
                table: "Media",
                newName: "IX_Media_Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Media",
                newName: "Kind");

            migrationBuilder.RenameIndex(
                name: "IX_Media_Type",
                table: "Media",
                newName: "IX_Media_Kind");
        }
    }
}
