using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddActivityRoutine : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "RoutineId",
            table: "Activities",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<char>(
            name: "RoutineWeek",
            table: "Activities",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Activities_RoutineId",
            table: "Activities",
            column: "RoutineId");

        migrationBuilder.AddForeignKey(
            name: "FK_Activities_Routines_RoutineId",
            table: "Activities",
            column: "RoutineId",
            principalTable: "Routines",
            principalColumn: "Id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Activities_Routines_RoutineId",
            table: "Activities");

        migrationBuilder.DropIndex(
            name: "IX_Activities_RoutineId",
            table: "Activities");

        migrationBuilder.DropColumn(
            name: "RoutineId",
            table: "Activities");

        migrationBuilder.DropColumn(
            name: "RoutineWeek",
            table: "Activities");
    }
}
