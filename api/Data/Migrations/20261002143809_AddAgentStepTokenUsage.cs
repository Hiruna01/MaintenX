using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentStepTokenUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletionTokens",
                table: "AgentSteps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptTokens",
                table: "AgentSteps",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionTokens",
                table: "AgentSteps");

            migrationBuilder.DropColumn(
                name: "PromptTokens",
                table: "AgentSteps");
        }
    }
}
