using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReleasePullRequestCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "ReleasePullRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleasePullRequests_Status",
                table: "ReleasePullRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReleasePullRequests_Status",
                table: "ReleasePullRequests");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ReleasePullRequests");
        }
    }
}
