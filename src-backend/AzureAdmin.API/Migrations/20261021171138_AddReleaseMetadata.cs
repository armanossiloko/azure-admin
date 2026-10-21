using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Releases",
                type: "uuid",
                nullable: true);

            // Give existing releases a stamp so concurrency checks also cover them. md5::uuid works on
            // PostgreSQL 9+ without pgcrypto / gen_random_uuid (13+). The creator of existing releases
            // is unknown and stays null — later edits must not invent one.
            migrationBuilder.Sql("""
                UPDATE "Releases"
                SET "ConcurrencyStamp" = md5(random()::text || "Id"::text || clock_timestamp()::text)::uuid
                WHERE "ConcurrencyStamp" IS NULL;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Releases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Releases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_CreatedByUserId",
                table: "Releases",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Releases_AspNetUsers_CreatedByUserId",
                table: "Releases",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Releases_AspNetUsers_CreatedByUserId",
                table: "Releases");

            migrationBuilder.DropIndex(
                name: "IX_Releases_CreatedByUserId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Releases");
        }
    }
}
