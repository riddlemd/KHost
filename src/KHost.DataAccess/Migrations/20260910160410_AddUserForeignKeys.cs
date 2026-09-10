using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KHost.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddUserForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserForeignKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsEphemeral = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserForeignKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserForeignKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserForeignKeys_IsEphemeral",
                table: "UserForeignKeys",
                column: "IsEphemeral");

            migrationBuilder.CreateIndex(
                name: "IX_UserForeignKeys_Source_Key",
                table: "UserForeignKeys",
                columns: new[] { "Source", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserForeignKeys_UserId",
                table: "UserForeignKeys",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserForeignKeys");
        }
    }
}
