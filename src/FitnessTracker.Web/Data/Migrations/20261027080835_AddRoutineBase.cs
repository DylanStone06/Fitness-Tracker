using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddRoutineBase : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BaseLocationId",
            table: "Routines",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Routines_BaseLocationId",
            table: "Routines",
            column: "BaseLocationId");

        migrationBuilder.AddForeignKey(
            name: "FK_Routines_Locations_BaseLocationId",
            table: "Routines",
            column: "BaseLocationId",
            principalTable: "Locations",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Routines_Locations_BaseLocationId",
            table: "Routines");

        migrationBuilder.DropIndex(
            name: "IX_Routines_BaseLocationId",
            table: "Routines");

        migrationBuilder.DropColumn(
            name: "BaseLocationId",
            table: "Routines");
    }
}
