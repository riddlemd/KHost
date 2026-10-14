using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTitleArtistFolded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtistFolded",
                table: "Media",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TitleFolded",
                table: "Media",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Type_ArtistFolded",
                table: "Media",
                columns: new[] { "Type", "ArtistFolded" });

            migrationBuilder.CreateIndex(
                name: "IX_Media_Type_TitleFolded",
                table: "Media",
                columns: new[] { "Type", "TitleFolded" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Media_Type_ArtistFolded",
                table: "Media");

            migrationBuilder.DropIndex(
                name: "IX_Media_Type_TitleFolded",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "ArtistFolded",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "TitleFolded",
                table: "Media");
        }
    }
}
