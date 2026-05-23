using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPatExpiresAtToAzureDevOpsPatCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PatExpiresAt",
                table: "AzureDevOpsPatCredentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PatExpiresAt",
                table: "AzureDevOpsPatCredentials");
        }
    }
}
