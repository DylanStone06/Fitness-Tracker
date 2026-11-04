using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddActivityGoal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "GoalId",
            table: "Activities",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Activities_GoalId",
            table: "Activities",
            column: "GoalId");

        migrationBuilder.AddForeignKey(
            name: "FK_Activities_WeeklyGoals_GoalId",
            table: "Activities",
            column: "GoalId",
            principalTable: "WeeklyGoals",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Activities_WeeklyGoals_GoalId",
            table: "Activities");

        migrationBuilder.DropIndex(
            name: "IX_Activities_GoalId",
            table: "Activities");

        migrationBuilder.DropColumn(
            name: "GoalId",
            table: "Activities");
    }
}
