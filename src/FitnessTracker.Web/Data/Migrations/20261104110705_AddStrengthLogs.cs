using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitnessTracker.Web.Data.Migrations;

/// <inheritdoc />
public partial class AddStrengthLogs : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Notes",
            table: "SessionLogs",
            type: "TEXT",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Exercises",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                NameKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Exercises", x => x.Id);
                table.ForeignKey(
                    name: "FK_Exercises_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SessionSets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<Guid>(type: "TEXT", nullable: false),
                SessionLogId = table.Column<Guid>(type: "TEXT", nullable: false),
                Position = table.Column<int>(type: "INTEGER", nullable: false),
                ExerciseId = table.Column<Guid>(type: "TEXT", nullable: false),
                Reps = table.Column<int>(type: "INTEGER", nullable: false),
                WeightGrams = table.Column<int>(type: "INTEGER", nullable: true),
                RestSeconds = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SessionSets", x => x.Id);
                table.ForeignKey(
                    name: "FK_SessionSets_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SessionSets_Exercises_ExerciseId",
                    column: x => x.ExerciseId,
                    principalTable: "Exercises",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_SessionSets_SessionLogs_SessionLogId",
                    column: x => x.SessionLogId,
                    principalTable: "SessionLogs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Exercises_OwnerId_NameKey",
            table: "Exercises",
            columns: new[] { "OwnerId", "NameKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SessionSets_ExerciseId",
            table: "SessionSets",
            column: "ExerciseId");

        migrationBuilder.CreateIndex(
            name: "IX_SessionSets_OwnerId",
            table: "SessionSets",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_SessionSets_SessionLogId_Position",
            table: "SessionSets",
            columns: new[] { "SessionLogId", "Position" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SessionSets");

        migrationBuilder.DropTable(
            name: "Exercises");

        migrationBuilder.DropColumn(
            name: "Notes",
            table: "SessionLogs");
    }
}
