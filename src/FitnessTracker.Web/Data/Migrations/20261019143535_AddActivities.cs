using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddActivities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Activities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Category = table.Column<int>(type: "INTEGER", nullable: false),
                LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                IsFixed = table.Column<bool>(type: "INTEGER", nullable: false),
                Priority = table.Column<int>(type: "INTEGER", nullable: false),
                IsDemanding = table.Column<bool>(type: "INTEGER", nullable: false),
                PreparationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                ChangingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                StartDate = table.Column<string>(type: "TEXT", nullable: false),
                StartTime = table.Column<string>(type: "TEXT", nullable: false),
                TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                LengthMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                RepeatRule = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Activities", x => x.Id);
                table.ForeignKey(
                    name: "FK_Activities_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Activities_Locations_LocationId",
                    column: x => x.LocationId,
                    principalTable: "Locations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Activities_LocationId",
            table: "Activities",
            column: "LocationId");

        migrationBuilder.CreateIndex(
            name: "IX_Activities_OwnerId",
            table: "Activities",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Activities");
    }
}
