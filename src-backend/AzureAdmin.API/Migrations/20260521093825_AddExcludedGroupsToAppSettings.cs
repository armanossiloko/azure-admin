using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddExcludedGroupsToAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConventionalCommitsShowOtherGroup",
                table: "AppSettings");

            migrationBuilder.AddColumn<string>(
                name: "ExcludedGroups",
                table: "AppSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedGroups",
                table: "AppSettings");

            migrationBuilder.AddColumn<bool>(
                name: "ConventionalCommitsShowOtherGroup",
                table: "AppSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
