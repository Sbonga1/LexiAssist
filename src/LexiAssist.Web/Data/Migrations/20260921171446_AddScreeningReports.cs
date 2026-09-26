using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreeningReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScreeningReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScreeningAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RiskTier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    FlaggedAreasJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    ScoringVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsReferralRecommended = table.Column<bool>(type: "INTEGER", nullable: false),
                    SkillAreasJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    RecommendedNextAction = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningReports_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningReports_ScreeningAttempts_ScreeningAttemptId",
                        column: x => x.ScreeningAttemptId,
                        principalTable: "ScreeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningReports_ScreeningAttemptId",
                table: "ScreeningReports",
                column: "ScreeningAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningReports_UserId",
                table: "ScreeningReports",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScreeningReports");
        }
    }
}
