using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtectScreeningResponsesFromDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "ScreeningResponses");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningTestAttempts_ScreeningAttemptId_ScreeningTestId",
                table: "ScreeningTestAttempts",
                columns: new[] { "ScreeningAttemptId", "ScreeningTestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "ScreeningResponses",
                columns: new[] { "ScreeningTestAttemptId", "ScreeningQuestionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScreeningTestAttempts_ScreeningAttemptId_ScreeningTestId",
                table: "ScreeningTestAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "ScreeningResponses");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningResponses_ScreeningTestAttemptId_ScreeningQuestionId",
                table: "ScreeningResponses",
                columns: new[] { "ScreeningTestAttemptId", "ScreeningQuestionId" });
        }
    }
}
