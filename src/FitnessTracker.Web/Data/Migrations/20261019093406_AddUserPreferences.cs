using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddUserPreferences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserPreferences",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                WeekStart = table.Column<int>(type: "INTEGER", nullable: false),
                Use24HourClock = table.Column<bool>(type: "INTEGER", nullable: false),
                DistanceUnit = table.Column<int>(type: "INTEGER", nullable: false),
                WeightUnit = table.Column<int>(type: "INTEGER", nullable: false),
                TargetSleepMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                MinimumSleepMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                UsualBedtime = table.Column<string>(type: "TEXT", nullable: false),
                UsualWakeTime = table.Column<string>(type: "TEXT", nullable: false),
                WindDownMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                WakeUpRoutineMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                PreferredExerciseDays = table.Column<int>(type: "INTEGER", nullable: false),
                PreferredTimeOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                RecoveryDays = table.Column<int>(type: "INTEGER", nullable: false),
                MinimumHoursBetweenDemandingSessions = table.Column<int>(type: "INTEGER", nullable: false),
                MaximumDemandingSessionsPerDay = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserPreferences", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserPreferences_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserPreferences_OwnerId",
            table: "UserPreferences",
            column: "OwnerId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UserPreferences");
    }
}
