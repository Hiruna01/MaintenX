using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReporterId = table.Column<int>(type: "integer", nullable: false),
                    RoomId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_Users_ReporterId",
                        column: x => x.ReporterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentWorkflows_ReportId",
                table: "AgentWorkflows",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterId",
                table: "Reports",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_RoomId",
                table: "Reports",
                column: "RoomId");

            // AgentWorkflows.ReportId existed before Reports did, and until now nothing
            // constrained it — the column was documented as "reports are a later feature",
            // so any value already in it points at a row that has never existed. Adding
            // the foreign key below fails on such a row (23503), which an empty CI database
            // never reveals but a developer's or a deployed database certainly does.
            //
            // So orphans are cleared to NULL first. This discards no real reference: by
            // definition every value it touches names a report that is not there. Written
            // as a predicate rather than a blanket UPDATE so the rule is stated in the
            // migration and stays correct if this ever runs against a populated Reports.
            migrationBuilder.Sql(
                """
                UPDATE "AgentWorkflows"
                SET "ReportId" = NULL
                WHERE "ReportId" IS NOT NULL
                  AND "ReportId" NOT IN (SELECT "Id" FROM "Reports");
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentWorkflows_Reports_ReportId",
                table: "AgentWorkflows",
                column: "ReportId",
                principalTable: "Reports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentWorkflows_Reports_ReportId",
                table: "AgentWorkflows");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_AgentWorkflows_ReportId",
                table: "AgentWorkflows");
        }
    }
}
