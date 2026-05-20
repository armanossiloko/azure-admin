using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class UserAzureDevOpsOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAzureDevOpsOrganizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OrganizationDisplay = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAzureDevOpsOrganizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAzureDevOpsOrganizations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAzureDevOpsOrganizations_UserId_OrganizationKey",
                table: "UserAzureDevOpsOrganizations",
                columns: new[] { "UserId", "OrganizationKey" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "UserAzureDevOpsOrganizations" ("Id", "UserId", "OrganizationKey", "OrganizationDisplay", "Notes", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), c."UserId", c."OrganizationKey", c."OrganizationDisplay", NULL, c."CreatedAt", c."UpdatedAt"
                FROM "AzureDevOpsPatCredentials" c
                WHERE NOT EXISTS (
                    SELECT 1 FROM "UserAzureDevOpsOrganizations" o
                    WHERE o."UserId" = c."UserId" AND o."OrganizationKey" = c."OrganizationKey");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAzureDevOpsOrganizations");
        }
    }
}
