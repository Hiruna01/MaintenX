using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentStepAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "AgentSteps",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "AgentSteps");
        }
    }
}
