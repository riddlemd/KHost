using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaPools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NameFolded = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    VenueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SelectionMode = table.Column<int>(type: "INTEGER", nullable: false),
                    NoRepeatCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AdTrigger = table.Column<int>(type: "INTEGER", nullable: false),
                    AdTriggerInterval = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaPools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaPoolEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaPoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChildPoolId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaPoolEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaPoolEntries_MediaPools_ChildPoolId",
                        column: x => x.ChildPoolId,
                        principalTable: "MediaPools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaPoolEntries_MediaPools_MediaPoolId",
                        column: x => x.MediaPoolId,
                        principalTable: "MediaPools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaPoolEntries_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaPoolEntries_ChildPoolId",
                table: "MediaPoolEntries",
                column: "ChildPoolId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPoolEntries_MediaId",
                table: "MediaPoolEntries",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPoolEntries_MediaPoolId",
                table: "MediaPoolEntries",
                column: "MediaPoolId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPools_Kind",
                table: "MediaPools",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPools_VenueId",
                table: "MediaPools",
                column: "VenueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaPoolEntries");

            migrationBuilder.DropTable(
                name: "MediaPools");
        }
    }
}
