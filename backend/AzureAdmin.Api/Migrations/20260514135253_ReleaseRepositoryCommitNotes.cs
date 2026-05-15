using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseRepositoryCommitNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseRepositoryCommitNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    SourceRefName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetRefName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CommitsJson = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseRepositoryCommitNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseRepositoryCommitNotes_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseRepositoryCommitNotes_RegisteredRepositories_Regis~",
                        column: x => x.RegisteredRepositoryId,
                        principalTable: "RegisteredRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseRepositoryCommitNotes_RegisteredRepositoryId",
                table: "ReleaseRepositoryCommitNotes",
                column: "RegisteredRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseRepositoryCommitNotes_ReleaseId_RegisteredRepositor~",
                table: "ReleaseRepositoryCommitNotes",
                columns: new[] { "ReleaseId", "RegisteredRepositoryId", "Phase" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseRepositoryCommitNotes");
        }
    }
}
