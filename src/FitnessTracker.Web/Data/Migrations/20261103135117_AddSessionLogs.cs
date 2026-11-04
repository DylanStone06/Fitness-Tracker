using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddSessionLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SessionLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                ActivityId = table.Column<Guid>(type: "TEXT", nullable: false),
                OccurrenceDate = table.Column<string>(type: "TEXT", nullable: false),
                Mark = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SessionLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_SessionLogs_Activities_ActivityId",
                    column: x => x.ActivityId,
                    principalTable: "Activities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SessionLogs_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SessionLogs_ActivityId_OccurrenceDate",
            table: "SessionLogs",
            columns: new[] { "ActivityId", "OccurrenceDate" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SessionLogs_OwnerId",
            table: "SessionLogs",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SessionLogs");
    }
}
