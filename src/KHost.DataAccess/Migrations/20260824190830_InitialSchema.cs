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
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageScaling = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SearchFolded = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    SampledHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Media", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaPools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<int>(type: "INTEGER", nullable: false),
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
                name: "Performances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SingerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VenueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    QueuePosition = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Performances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NameFolded = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ExcludeFromSingerQueue = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    Permissions = table.Column<string>(type: "JSON", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NameFolded = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
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
                    NameFolded = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
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
                    AudioMediaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AudioStart = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: true),
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

            migrationBuilder.CreateTable(
                name: "Tips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VenueId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AmountInCents = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NotesFolded = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tips_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGroupMemberships",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroupMemberships", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_UserGroupMemberships_UserGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroupMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "UserGroups",
                columns: new[] { "Id", "Description", "ExcludeFromSingerQueue", "IsAdmin", "Name", "NameFolded", "Permissions" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), "Full access to all management features", true, true, "Admin", "admin", "[]" });

            migrationBuilder.InsertData(
                table: "UserGroups",
                columns: new[] { "Id", "Description", "Name", "NameFolded", "Permissions" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000002"), "Frequent singer", "Regular", "regular", "[]" });

            migrationBuilder.CreateIndex(
                name: "IX_Media_Artist",
                table: "Media",
                column: "Artist");

            migrationBuilder.CreateIndex(
                name: "IX_Media_ContentHash",
                table: "Media",
                column: "ContentHash");

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
                name: "IX_Media_FileSize",
                table: "Media",
                column: "FileSize");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Status",
                table: "Media",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Title",
                table: "Media",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Type",
                table: "Media",
                column: "Type");

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
                name: "IX_MediaPools_Purpose",
                table: "MediaPools",
                column: "Purpose");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPools_VenueId",
                table: "MediaPools",
                column: "VenueId");

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
                name: "IX_Performances_VenueId",
                table: "Performances",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_Tips_CreatedDate",
                table: "Tips",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Tips_UserId",
                table: "Tips",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tips_VenueId",
                table: "Tips",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMemberships_UserId",
                table: "UserGroupMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_Name",
                table: "UserGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedDate",
                table: "Users",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NameFolded",
                table: "Users",
                column: "NameFolded",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Venues_Name",
                table: "Venues",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaPoolEntries");

            migrationBuilder.DropTable(
                name: "Performances");

            migrationBuilder.DropTable(
                name: "Tips");

            migrationBuilder.DropTable(
                name: "UserGroupMemberships");

            migrationBuilder.DropTable(
                name: "Venues");

            migrationBuilder.DropTable(
                name: "MediaPools");

            migrationBuilder.DropTable(
                name: "Media");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
