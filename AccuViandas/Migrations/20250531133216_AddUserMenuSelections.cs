using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccuViandas.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMenuSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserMenuSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyMenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedCategory = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectionDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMenuSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMenuSelections_DailyMenus_DailyMenuId",
                        column: x => x.DailyMenuId,
                        principalTable: "DailyMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserMenuSelections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserMenuSelections_DailyMenuId",
                table: "UserMenuSelections",
                column: "DailyMenuId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMenuSelections_UserId_DailyMenuId_IsActive",
                table: "UserMenuSelections",
                columns: new[] { "UserId", "DailyMenuId", "IsActive" },
                unique: true,
                filter: "IsActive = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserMenuSelections");
        }
    }
}
