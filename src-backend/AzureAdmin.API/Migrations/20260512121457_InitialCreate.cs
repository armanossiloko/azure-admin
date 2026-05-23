using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SprintLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ParentTeamId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Teams_ParentTeamId",
                        column: x => x.ParentTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RegisteredRepositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AzureDevOpsOrganization = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AzureDevOpsProject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RepositoryIdOrName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegisteredRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegisteredRepositories_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseTeams_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleaseTeams_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleasePullRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegisteredRepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    AzureDevOpsPullRequestId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceRefName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetRefName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleasePullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleasePullRequests_RegisteredRepositories_RegisteredReposi~",
                        column: x => x.RegisteredRepositoryId,
                        principalTable: "RegisteredRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleasePullRequests_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReleasePullRequests_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredRepositories_AzureDevOpsOrganization_AzureDevOpsP~",
                table: "RegisteredRepositories",
                columns: new[] { "AzureDevOpsOrganization", "AzureDevOpsProject", "RepositoryIdOrName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegisteredRepositories_TeamId",
                table: "RegisteredRepositories",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleasePullRequests_RegisteredRepositoryId",
                table: "ReleasePullRequests",
                column: "RegisteredRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleasePullRequests_ReleaseId_RegisteredRepositoryId_Phase",
                table: "ReleasePullRequests",
                columns: new[] { "ReleaseId", "RegisteredRepositoryId", "Phase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleasePullRequests_TeamId",
                table: "ReleasePullRequests",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseTeams_ReleaseId_TeamId",
                table: "ReleaseTeams",
                columns: new[] { "ReleaseId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseTeams_TeamId",
                table: "ReleaseTeams",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ParentTeamId",
                table: "Teams",
                column: "ParentTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleasePullRequests");

            migrationBuilder.DropTable(
                name: "ReleaseTeams");

            migrationBuilder.DropTable(
                name: "RegisteredRepositories");

            migrationBuilder.DropTable(
                name: "Releases");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
