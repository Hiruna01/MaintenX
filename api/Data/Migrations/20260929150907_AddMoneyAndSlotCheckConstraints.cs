using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneyAndSlotCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A CHECK constraint is validated against every row already in the table, so on a
            // database that holds a violating row the ALTER TABLE fails and the migration is
            // unrunnable — while CI, which migrates an EMPTY database, stays green. The rows
            // that would fail are repaired first, the way AddWorkOrders clears orphaned
            // ServiceRecords.WorkOrderId values. No endpoint could have written any of them
            // (every DTO and SlotRules refuse them), so on every real database this changes
            // nothing; it is written as predicates so the rule is stated, not the situation.
            //
            //   * a class that does not end after it starts: deleted. The table is a mirror of
            //     Google Calendar, a zero-length or backwards class blocks no slot anyway, and
            //     the next sync writes back any event that really is a class.
            //   * a booked visit that does not end after it starts: deleted. It overlaps
            //     nothing, so no slot decision ever read it; there is no true time to repair it to.
            //   * a negative actual cost: NULL — "not recorded", which is what an actual cost
            //     no one could have entered is.
            //   * a negative estimate: 0. The column is not nullable, and the order has
            //     already been through the approval gate; this does not re-route it.
            migrationBuilder.Sql(
                """
                DELETE FROM "ClassScheduleSlots" WHERE "EndsAt" <= "StartsAt";
                DELETE FROM "ScheduledSlots" WHERE "EndsAt" <= "StartsAt";
                UPDATE "WorkOrders" SET "ActualCost" = NULL WHERE "ActualCost" < 0;
                UPDATE "WorkOrders" SET "EstimatedCost" = 0 WHERE "EstimatedCost" < 0;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrders_ActualCost_NotNegative",
                table: "WorkOrders",
                sql: "\"ActualCost\" IS NULL OR \"ActualCost\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkOrders_EstimatedCost_NotNegative",
                table: "WorkOrders",
                sql: "\"EstimatedCost\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ScheduledSlots_EndsAfterStart",
                table: "ScheduledSlots",
                sql: "\"EndsAt\" > \"StartsAt\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClassScheduleSlots_EndsAfterStart",
                table: "ClassScheduleSlots",
                sql: "\"EndsAt\" > \"StartsAt\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrders_ActualCost_NotNegative",
                table: "WorkOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkOrders_EstimatedCost_NotNegative",
                table: "WorkOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ScheduledSlots_EndsAfterStart",
                table: "ScheduledSlots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClassScheduleSlots_EndsAfterStart",
                table: "ClassScheduleSlots");
        }
    }
}
