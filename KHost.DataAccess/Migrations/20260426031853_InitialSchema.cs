using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Media", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Performances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SingerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueuePosition = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Performances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsTipper = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRegular = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultVenueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DefaultVolume = table.Column<int>(type: "INTEGER", nullable: false),
                    MoveSingerToBottomAfterPerformance = table.Column<bool>(type: "INTEGER", nullable: false),
                    PromptBeforeRemovingSinger = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClearQueueOnClose = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Media_Artist",
                table: "Media",
                column: "Artist");

            migrationBuilder.CreateIndex(
                name: "IX_Media_DateAdded",
                table: "Media",
                column: "DateAdded");

            migrationBuilder.CreateIndex(
                name: "IX_Media_FilePath",
                table: "Media",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Media_Status",
                table: "Media",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Title",
                table: "Media",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Performances_CreatedDate",
                table: "Performances",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Performances_MediaId",
                table: "Performances",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Performances_SingerId",
                table: "Performances",
                column: "SingerId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedDate",
                table: "Users",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsAdmin",
                table: "Users",
                column: "IsAdmin");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Name",
                table: "Users",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Venues_Name",
                table: "Venues",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Media");

            migrationBuilder.DropTable(
                name: "Performances");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Venues");
        }
    }
}
