using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzureAdmin.API.Migrations
{
    /// <inheritdoc />
    public partial class RefineWebhookSubscriptionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnDevToMasterCreated",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnDevToMasterMergedNew",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnMasterToProdCreatedNew",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnMasterToProdMergedNew",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateCreatedNew",
                table: "WebhookSubscriptions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateMergedNew",
                table: "WebhookSubscriptions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "WebhookSubscriptions" SET
                    "NotifyOnDevToMasterCreated" = "NotifyOnPrBatchCreated",
                    "NotifyOnMasterToProdCreatedNew" = "NotifyOnPrBatchCreated",
                    "NotifyOnDevToMasterMergedNew" = "NotifyOnPrBatchCompleted" OR "NotifyOnPrPhaseComplete",
                    "NotifyOnMasterToProdMergedNew" = "NotifyOnPrBatchCompleted" OR "NotifyOnPrPhaseComplete",
                    "TemplateCreatedNew" = "TemplatePrBatchCreated",
                    "TemplateMergedNew" = COALESCE("TemplatePrBatchCompleted", "TemplatePrPhaseComplete");
                """);

            migrationBuilder.DropColumn(name: "NotifyOnPrBatchCreated", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "NotifyOnPrBatchCompleted", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "NotifyOnPrPhaseComplete", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "TemplatePrBatchCreated", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "TemplatePrBatchCompleted", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "TemplatePrPhaseComplete", table: "WebhookSubscriptions");

            migrationBuilder.RenameColumn(
                name: "NotifyOnDevToMasterMergedNew",
                table: "WebhookSubscriptions",
                newName: "NotifyOnDevToMasterMerged");
            migrationBuilder.RenameColumn(
                name: "NotifyOnMasterToProdCreatedNew",
                table: "WebhookSubscriptions",
                newName: "NotifyOnMasterToProdCreated");
            migrationBuilder.RenameColumn(
                name: "NotifyOnMasterToProdMergedNew",
                table: "WebhookSubscriptions",
                newName: "NotifyOnMasterToProdMerged");
            migrationBuilder.RenameColumn(
                name: "TemplateCreatedNew",
                table: "WebhookSubscriptions",
                newName: "TemplateCreated");
            migrationBuilder.RenameColumn(
                name: "TemplateMergedNew",
                table: "WebhookSubscriptions",
                newName: "TemplateMerged");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPrBatchCreated",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPrBatchCompleted",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPrPhaseComplete",
                table: "WebhookSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
            migrationBuilder.AddColumn<string>(
                name: "TemplatePrBatchCreated",
                table: "WebhookSubscriptions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "TemplatePrBatchCompleted",
                table: "WebhookSubscriptions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "TemplatePrPhaseComplete",
                table: "WebhookSubscriptions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "WebhookSubscriptions" SET
                    "NotifyOnPrBatchCreated" = "NotifyOnDevToMasterCreated" OR "NotifyOnMasterToProdCreated",
                    "NotifyOnPrBatchCompleted" = "NotifyOnDevToMasterMerged" OR "NotifyOnMasterToProdMerged",
                    "NotifyOnPrPhaseComplete" = "NotifyOnDevToMasterMerged" OR "NotifyOnMasterToProdMerged",
                    "TemplatePrBatchCreated" = "TemplateCreated",
                    "TemplatePrBatchCompleted" = "TemplateMerged";
                """);

            migrationBuilder.DropColumn(name: "NotifyOnDevToMasterCreated", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "NotifyOnDevToMasterMerged", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "NotifyOnMasterToProdCreated", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "NotifyOnMasterToProdMerged", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "TemplateCreated", table: "WebhookSubscriptions");
            migrationBuilder.DropColumn(name: "TemplateMerged", table: "WebhookSubscriptions");
        }
    }
}
