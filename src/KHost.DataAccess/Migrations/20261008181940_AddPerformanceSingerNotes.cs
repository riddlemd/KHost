using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceSingerNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SingerNotes",
                table: "Performances",
                type: "TEXT",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SingerNotes",
                table: "Performances");
        }
    }
}
