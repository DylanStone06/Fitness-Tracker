using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddOtherActivityLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CustomOtherType",
            table: "SessionLogs",
            type: "TEXT",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OtherType",
            table: "SessionLogs",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CustomOtherType",
            table: "SessionLogs");

        migrationBuilder.DropColumn(
            name: "OtherType",
            table: "SessionLogs");
    }
}
