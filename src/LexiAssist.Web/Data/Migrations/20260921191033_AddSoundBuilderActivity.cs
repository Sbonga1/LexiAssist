using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoundBuilderActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "LearningActivities",
                columns: new[] { "Id", "ActivityType", "Description", "Instructions", "IsActive", "Name" },
                values: new object[] { 1, "PhonologicalAwareness", "Practice blending and segmenting sound chunks in short words.", "Arrange the chunks, then blend or segment the word at your own pace.", true, "Sound Builder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttempts_OneInProgressPerAssignment",
                table: "ActivityAttempts",
                column: "ActivityAssignmentId",
                unique: true,
                filter: "\"CompletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityAttempts_OneInProgressPerAssignment",
                table: "ActivityAttempts");

            migrationBuilder.DeleteData(
                table: "LearningActivities",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
