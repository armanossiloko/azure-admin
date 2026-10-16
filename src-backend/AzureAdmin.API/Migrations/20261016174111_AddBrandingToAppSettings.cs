using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingToAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandingApplicationName",
                table: "AppSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandingDescription",
                table: "AppSettings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandingTagline",
                table: "AppSettings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandingApplicationName",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BrandingDescription",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BrandingTagline",
                table: "AppSettings");
        }
    }
}
