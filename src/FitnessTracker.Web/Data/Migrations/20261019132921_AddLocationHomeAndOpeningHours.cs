using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddLocationHomeAndOpeningHours : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "HasOpeningHours",
            table: "Locations",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IsHome",
            table: "Locations",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "LocationOpeningTimes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                Day = table.Column<int>(type: "INTEGER", nullable: false),
                Opens = table.Column<string>(type: "TEXT", nullable: false),
                Closes = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LocationOpeningTimes", x => x.Id);
                table.ForeignKey(
                    name: "FK_LocationOpeningTimes_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_LocationOpeningTimes_Locations_LocationId",
                    column: x => x.LocationId,
                    principalTable: "Locations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LocationOpeningTimes_LocationId_Day",
            table: "LocationOpeningTimes",
            columns: new[] { "LocationId", "Day" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LocationOpeningTimes_OwnerId",
            table: "LocationOpeningTimes",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LocationOpeningTimes");

        migrationBuilder.DropColumn(
            name: "HasOpeningHours",
            table: "Locations");

        migrationBuilder.DropColumn(
            name: "IsHome",
            table: "Locations");
    }
}
