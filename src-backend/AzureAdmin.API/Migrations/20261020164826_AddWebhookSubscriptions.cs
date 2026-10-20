using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ProtectedUrl = table.Column<byte[]>(type: "bytea", nullable: false),
                    UrlPreview = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NotifyOnPrBatchCreated = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnPrBatchCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnPrPhaseComplete = table.Column<bool>(type: "boolean", nullable: false),
                    TemplatePrBatchCreated = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TemplatePrBatchCompleted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TemplatePrPhaseComplete = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastDeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDeliveryError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_Enabled",
                table: "WebhookSubscriptions",
                column: "Enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");
        }
    }
}
