using System;
using LexiAssist.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

public partial class AddAssessmentDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AssessmentDrafts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ScreeningTestId = table.Column<int>(type: "INTEGER", nullable: false),
                IsCsvImport = table.Column<bool>(type: "INTEGER", nullable: false),
                ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                IsDiscarded = table.Column<bool>(type: "INTEGER", nullable: false),
                Version = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssessmentDrafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssessmentDrafts_ScreeningTests_ScreeningTestId",
                    column: x => x.ScreeningTestId,
                    principalTable: "ScreeningTests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AssessmentDrafts_ScreeningTestId_IsDiscarded",
            table: "AssessmentDrafts",
            columns: new[] { "ScreeningTestId", "IsDiscarded" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AssessmentDrafts");
    }
}
