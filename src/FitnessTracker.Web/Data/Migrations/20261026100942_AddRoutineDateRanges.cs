using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddRoutineDateRanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RoutineDateRanges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                RoutineId = table.Column<Guid>(type: "TEXT", nullable: false),
                StartDate = table.Column<string>(type: "TEXT", nullable: false),
                EndDate = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoutineDateRanges", x => x.Id);
                table.ForeignKey(
                    name: "FK_RoutineDateRanges_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_RoutineDateRanges_Routines_RoutineId",
                    column: x => x.RoutineId,
                    principalTable: "Routines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RoutineDateRanges_OwnerId",
            table: "RoutineDateRanges",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_RoutineDateRanges_RoutineId",
            table: "RoutineDateRanges",
            column: "RoutineId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RoutineDateRanges");
    }
}
