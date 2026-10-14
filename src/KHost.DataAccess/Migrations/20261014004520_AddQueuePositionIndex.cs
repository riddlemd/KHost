using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuePositionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Performances_QueuePosition",
                table: "Performances",
                column: "QueuePosition",
                filter: "\"QueuePosition\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Performances_QueuePosition",
                table: "Performances");
        }
    }
}
