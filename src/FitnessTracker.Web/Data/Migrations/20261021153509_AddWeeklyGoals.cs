using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddWeeklyGoals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WeeklyGoals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Category = table.Column<int>(type: "INTEGER", nullable: false),
                LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                SessionsPerWeek = table.Column<int>(type: "INTEGER", nullable: false),
                SessionMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                PreparationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                ChangingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                IsDemanding = table.Column<bool>(type: "INTEGER", nullable: false),
                Priority = table.Column<int>(type: "INTEGER", nullable: false),
                PreferredDays = table.Column<int>(type: "INTEGER", nullable: false),
                PreferredTimeOfDay = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeeklyGoals", x => x.Id);
                table.ForeignKey(
                    name: "FK_WeeklyGoals_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_WeeklyGoals_Locations_LocationId",
                    column: x => x.LocationId,
                    principalTable: "Locations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyGoals_LocationId",
            table: "WeeklyGoals",
            column: "LocationId");

        migrationBuilder.CreateIndex(
            name: "IX_WeeklyGoals_OwnerId",
            table: "WeeklyGoals",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WeeklyGoals");
    }
}
