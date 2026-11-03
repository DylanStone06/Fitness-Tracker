using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddRunLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ActualSeconds",
            table: "SessionLogs",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CustomRunType",
            table: "SessionLogs",
            type: "TEXT",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DistanceMetres",
            table: "SessionLogs",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Effort",
            table: "SessionLogs",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "RunType",
            table: "SessionLogs",
            type: "INTEGER",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ActualSeconds",
            table: "SessionLogs");

        migrationBuilder.DropColumn(
            name: "CustomRunType",
            table: "SessionLogs");

        migrationBuilder.DropColumn(
            name: "DistanceMetres",
            table: "SessionLogs");

        migrationBuilder.DropColumn(
            name: "Effort",
            table: "SessionLogs");

        migrationBuilder.DropColumn(
            name: "RunType",
            table: "SessionLogs");
    }
}
