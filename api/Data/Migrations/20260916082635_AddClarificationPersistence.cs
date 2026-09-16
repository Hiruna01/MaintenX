using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClarificationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssetId",
                table: "Reports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "Reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClarificationQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<int>(type: "integer", nullable: false),
                    WorkflowId = table.Column<int>(type: "integer", nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AnswerType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OptionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClarificationQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClarificationQuestions_AgentWorkflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "AgentWorkflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClarificationQuestions_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClarificationAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClarificationQuestionId = table.Column<int>(type: "integer", nullable: false),
                    AnswerText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AnsweredByUserId = table.Column<int>(type: "integer", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClarificationAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClarificationAnswers_ClarificationQuestions_ClarificationQu~",
                        column: x => x.ClarificationQuestionId,
                        principalTable: "ClarificationQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClarificationAnswers_Users_AnsweredByUserId",
                        column: x => x.AnsweredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_AssetId",
                table: "Reports",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationAnswers_AnsweredByUserId",
                table: "ClarificationAnswers",
                column: "AnsweredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationAnswers_ClarificationQuestionId",
                table: "ClarificationAnswers",
                column: "ClarificationQuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationQuestions_ReportId",
                table: "ClarificationQuestions",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ClarificationQuestions_WorkflowId",
                table: "ClarificationQuestions",
                column: "WorkflowId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Assets_AssetId",
                table: "Reports",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Assets_AssetId",
                table: "Reports");

            migrationBuilder.DropTable(
                name: "ClarificationAnswers");

            migrationBuilder.DropTable(
                name: "ClarificationQuestions");

            migrationBuilder.DropIndex(
                name: "IX_Reports_AssetId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "Reports");
        }
    }
}
