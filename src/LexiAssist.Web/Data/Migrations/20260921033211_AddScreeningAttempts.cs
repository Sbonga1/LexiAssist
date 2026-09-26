using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreeningAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScreeningAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OverallScore = table.Column<double>(type: "REAL", nullable: true),
                    RiskTier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CurrentTestId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningAttempts_ScreeningTests_CurrentTestId",
                        column: x => x.CurrentTestId,
                        principalTable: "ScreeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningTestAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScreeningAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScreeningTestId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RawScore = table.Column<double>(type: "REAL", nullable: true),
                    PercentageScore = table.Column<double>(type: "REAL", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningTestAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningTestAttempts_ScreeningAttempts_ScreeningAttemptId",
                        column: x => x.ScreeningAttemptId,
                        principalTable: "ScreeningAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningTestAttempts_ScreeningTests_ScreeningTestId",
                        column: x => x.ScreeningTestId,
                        principalTable: "ScreeningTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScreeningTestAttemptId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScreeningQuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TextAnswer = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    SelectedAnswer = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: true),
                    Score = table.Column<double>(type: "REAL", nullable: true),
                    ResponseTimeMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    AudioRecordingPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreeningResponses_ScreeningQuestions_ScreeningQuestionId",
                        column: x => x.ScreeningQuestionId,
                        principalTable: "ScreeningQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScreeningResponses_ScreeningTestAttempts_ScreeningTestAttemptId",
                        column: x => x.ScreeningTestAttemptId,
                        principalTable: "ScreeningTestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningAttempts_CurrentTestId",
                table: "ScreeningAttempts",
                column: "CurrentTestId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningAttempts_UserId_StartedAtUtc",
                table: "ScreeningAttempts",
                columns: new[] { "UserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResponses_ScreeningQuestionId",
                table: "ScreeningResponses",
                column: "ScreeningQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "ScreeningResponses",
                columns: new[] { "ScreeningTestAttemptId", "ScreeningQuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTestAttempts_ScreeningAttemptId_StartedAtUtc",
                table: "ScreeningTestAttempts",
                columns: new[] { "ScreeningAttemptId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTestAttempts_ScreeningTestId",
                table: "ScreeningTestAttempts",
                column: "ScreeningTestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScreeningResponses");

            migrationBuilder.DropTable(
                name: "ScreeningTestAttempts");

            migrationBuilder.DropTable(
                name: "ScreeningAttempts");
        }
    }
}
