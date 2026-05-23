using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConventionalCommitsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConventionalCommitsUseEmojis = table.Column<bool>(type: "boolean", nullable: false),
                    ConventionalCommitsShowOtherGroup = table.Column<bool>(type: "boolean", nullable: false),
                    JiraEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    JiraBaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    JiraProjectKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
