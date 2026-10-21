using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReleasePullRequestCreatorForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The column existed without a constraint; drop references to users that no longer exist so the
            // foreign key can be added (those PRs fall back to any PAT holder, as for pre-column rows).
            migrationBuilder.Sql("""
                UPDATE "ReleasePullRequests" AS pr SET "CreatedByUserId" = NULL
                WHERE pr."CreatedByUserId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "AspNetUsers" AS u WHERE u."Id" = pr."CreatedByUserId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReleasePullRequests_CreatedByUserId",
                table: "ReleasePullRequests",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReleasePullRequests_AspNetUsers_CreatedByUserId",
                table: "ReleasePullRequests",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReleasePullRequests_AspNetUsers_CreatedByUserId",
                table: "ReleasePullRequests");

            migrationBuilder.DropIndex(
                name: "IX_ReleasePullRequests_CreatedByUserId",
                table: "ReleasePullRequests");
        }
    }
}
