using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScreeningReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    LearnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    AssignedPsychologistId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Referrals_AspNetUsers_AssignedPsychologistId",
                        column: x => x.AssignedPsychologistId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_AspNetUsers_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_ScreeningReports_ScreeningReportId",
                        column: x => x.ScreeningReportId,
                        principalTable: "ScreeningReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_AssignedPsychologistId_Status_CreatedAtUtc",
                table: "Referrals",
                columns: new[] { "AssignedPsychologistId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_LearnerId_CreatedAtUtc",
                table: "Referrals",
                columns: new[] { "LearnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ScreeningReportId",
                table: "Referrals",
                column: "ScreeningReportId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "Referrals"
                    ("ScreeningReportId", "LearnerId", "AssignedPsychologistId", "Status", "CreatedAtUtc", "ReviewedAtUtc", "Notes")
                SELECT report."Id", report."UserId", NULL, 'Pending', report."GeneratedAtUtc", NULL, NULL
                FROM "ScreeningReports" AS report
                INNER JOIN "ScreeningAttempts" AS attempt ON attempt."Id" = report."ScreeningAttemptId"
                WHERE report."RiskTier" = 'Red'
                  AND report."IsReferralRecommended" = 1
                  AND attempt."Status" = 2
                  AND attempt."CompletedAtUtc" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "Referrals" AS referral
                      WHERE referral."ScreeningReportId" = report."Id")
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Referrals");
        }
    }
}
