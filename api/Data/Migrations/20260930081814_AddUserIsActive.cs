using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampusFacilities.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited from the generated `false`: every account that exists when this
            // runs was able to sign in a moment ago and must still be able to. The model
            // deliberately declares no database default (see AppDbContext), so this value
            // only fills existing rows; new rows get true from User's property initializer.
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");
        }
    }
}
