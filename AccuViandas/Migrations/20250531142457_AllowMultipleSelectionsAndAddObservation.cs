using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccuViandas.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleSelectionsAndAddObservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMenuSelections_UserId_DailyMenuId_IsActive",
                table: "UserMenuSelections");

            migrationBuilder.AddColumn<string>(
                name: "Observation",
                table: "UserMenuSelections",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_UserMenuSelections_UserId",
                table: "UserMenuSelections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMenuSelections_UserId",
                table: "UserMenuSelections");

            migrationBuilder.DropColumn(
                name: "Observation",
                table: "UserMenuSelections");

            migrationBuilder.CreateIndex(
                name: "IX_UserMenuSelections_UserId_DailyMenuId_IsActive",
                table: "UserMenuSelections",
                columns: new[] { "UserId", "DailyMenuId", "IsActive" },
                unique: true,
                filter: "IsActive = 1");
        }
    }
}
