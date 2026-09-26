using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreeningRiskClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RiskCalculatedAtUtc",
                table: "ScreeningAttempts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiskCalculationVersion",
                table: "ScreeningAttempts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RiskCalculatedAtUtc",
                table: "ScreeningAttempts");

            migrationBuilder.DropColumn(
                name: "RiskCalculationVersion",
                table: "ScreeningAttempts");
        }
    }
}
