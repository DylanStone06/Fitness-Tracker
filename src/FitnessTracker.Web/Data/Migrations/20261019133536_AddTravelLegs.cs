using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddTravelLegs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TravelLegs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                FromLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                ToLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                Mode = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TravelLegs", x => x.Id);
                table.ForeignKey(
                    name: "FK_TravelLegs_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TravelLegs_Locations_FromLocationId",
                    column: x => x.FromLocationId,
                    principalTable: "Locations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TravelLegs_Locations_ToLocationId",
                    column: x => x.ToLocationId,
                    principalTable: "Locations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TravelLegs_FromLocationId_ToLocationId",
            table: "TravelLegs",
            columns: new[] { "FromLocationId", "ToLocationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TravelLegs_OwnerId",
            table: "TravelLegs",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_TravelLegs_ToLocationId",
            table: "TravelLegs",
            column: "ToLocationId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TravelLegs");
    }
}
