using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AgentQueuedAt",
                table: "VerificationChecks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpiredReason",
                table: "VerificationChecks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentQueuedAt",
                table: "VerificationChecks");

            migrationBuilder.DropColumn(
                name: "ExpiredReason",
                table: "VerificationChecks");
        }
    }
}
