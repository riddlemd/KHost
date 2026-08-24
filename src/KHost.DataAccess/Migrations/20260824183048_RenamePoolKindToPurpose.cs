using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RenamePoolKindToPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Kind",
                table: "MediaPools",
                newName: "Purpose");

            migrationBuilder.RenameIndex(
                name: "IX_MediaPools_Kind",
                table: "MediaPools",
                newName: "IX_MediaPools_Purpose");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Purpose",
                table: "MediaPools",
                newName: "Kind");

            migrationBuilder.RenameIndex(
                name: "IX_MediaPools_Purpose",
                table: "MediaPools",
                newName: "IX_MediaPools_Kind");
        }
    }
}
