using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureDevOpsPatCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AzureDevOpsPatCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrganizationDisplay = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProtectedPat = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureDevOpsPatCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AzureDevOpsPatCredentials_OrganizationKey",
                table: "AzureDevOpsPatCredentials",
                column: "OrganizationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AzureDevOpsPatCredentials");
        }
    }
}
