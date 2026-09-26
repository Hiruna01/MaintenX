using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowReopenedWorkOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReopenedWorkOrderId",
                table: "AgentWorkflows",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflows_ReopenedWorkOrderId",
                table: "AgentWorkflows",
                column: "ReopenedWorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentWorkflows_WorkOrders_ReopenedWorkOrderId",
                table: "AgentWorkflows",
                column: "ReopenedWorkOrderId",
                principalTable: "WorkOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentWorkflows_WorkOrders_ReopenedWorkOrderId",
                table: "AgentWorkflows");

            migrationBuilder.DropIndex(
                name: "IX_AgentWorkflows_ReopenedWorkOrderId",
                table: "AgentWorkflows");

            migrationBuilder.DropColumn(
                name: "ReopenedWorkOrderId",
                table: "AgentWorkflows");
        }
    }
}
