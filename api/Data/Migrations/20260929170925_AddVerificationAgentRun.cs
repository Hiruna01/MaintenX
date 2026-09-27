using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationAgentRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgentAttempts",
                table: "VerificationChecks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AgentError",
                table: "VerificationChecks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentJudgedAt",
                table: "VerificationChecks",
                type: "timestamp with time zone",
                nullable: true);

            // A check that already carries a verdict was judged — by the seeder, since nothing
            // else wrote one before this. Without a judgement stamp the runner would read it as
            // waiting and ask the agent again, overwriting the verdict. Queued (if it never was)
            // and judged at the same instant, so it is not waiting: the test is judged < queued.
            migrationBuilder.Sql(
                """
                UPDATE "VerificationChecks"
                SET "AgentQueuedAt" = COALESCE("AgentQueuedAt", "ReporterRespondedAt", "UpdatedAt")
                WHERE "AgentOutcome" IS NOT NULL;
                UPDATE "VerificationChecks"
                SET "AgentJudgedAt" = "AgentQueuedAt", "AgentAttempts" = 1
                WHERE "AgentOutcome" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentAttempts",
                table: "VerificationChecks");

            migrationBuilder.DropColumn(
                name: "AgentError",
                table: "VerificationChecks");

            migrationBuilder.DropColumn(
                name: "AgentJudgedAt",
                table: "VerificationChecks");
        }
    }
}
