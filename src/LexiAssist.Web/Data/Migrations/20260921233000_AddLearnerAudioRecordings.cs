using LexiAssist.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260921233000_AddLearnerAudioRecordings")]
public partial class AddLearnerAudioRecordings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE LearningActivities SET Instructions = 'Read each displayed word aloud and choose how the practice try went. Speech is not automatically evaluated; optional recording requires your consent.' WHERE Id = 3;");

        migrationBuilder.AddColumn<bool>(
            name: "AllowsAudioRecording",
            table: "LearningActivities",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "AllowsAudioRecording",
            table: "ScreeningQuestions",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "LearnerAudioRecordings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                ScreeningTestAttemptId = table.Column<int>(type: "INTEGER", nullable: true),
                ScreeningQuestionId = table.Column<int>(type: "INTEGER", nullable: true),
                ActivityAttemptId = table.Column<int>(type: "INTEGER", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ConsentAcceptedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                ConsentNoticeVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UploadedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                ContentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                StorageFileName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                Status = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LearnerAudioRecordings", row => row.Id);
                table.CheckConstraint(
                    "CK_LearnerAudioRecordings_OneTarget",
                    "(\"ActivityAttemptId\" IS NOT NULL AND \"ScreeningTestAttemptId\" IS NULL AND \"ScreeningQuestionId\" IS NULL) OR " +
                    "(\"ActivityAttemptId\" IS NULL AND \"ScreeningTestAttemptId\" IS NOT NULL AND \"ScreeningQuestionId\" IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_LearnerAudioRecordings_ActivityAttempts_ActivityAttemptId",
                    column: row => row.ActivityAttemptId,
                    principalTable: "ActivityAttempts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerAudioRecordings_AspNetUsers_UserId",
                    column: row => row.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerAudioRecordings_ScreeningQuestions_ScreeningQuestionId",
                    column: row => row.ScreeningQuestionId,
                    principalTable: "ScreeningQuestions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LearnerAudioRecordings_ScreeningTestAttempts_ScreeningTestAttemptId",
                    column: row => row.ScreeningTestAttemptId,
                    principalTable: "ScreeningTestAttempts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LearnerAudioRecordings_ActivityAttemptId",
            table: "LearnerAudioRecordings",
            column: "ActivityAttemptId",
            unique: true,
            filter: "\"Status\" = 0 AND \"ActivityAttemptId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerAudioRecordings_ScreeningTestAttemptId_ScreeningQuestionId",
            table: "LearnerAudioRecordings",
            columns: new[] { "ScreeningTestAttemptId", "ScreeningQuestionId" },
            unique: true,
            filter: "\"Status\" = 0 AND \"ScreeningTestAttemptId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerAudioRecordings_ScreeningQuestionId",
            table: "LearnerAudioRecordings",
            column: "ScreeningQuestionId");

        migrationBuilder.CreateIndex(
            name: "IX_LearnerAudioRecordings_StorageFileName",
            table: "LearnerAudioRecordings",
            column: "StorageFileName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LearnerAudioRecordings_UserId_CreatedAtUtc",
            table: "LearnerAudioRecordings",
            columns: new[] { "UserId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LearnerAudioRecordings");
        migrationBuilder.DropColumn(name: "AllowsAudioRecording", table: "LearningActivities");
        migrationBuilder.DropColumn(name: "AllowsAudioRecording", table: "ScreeningQuestions");
        migrationBuilder.Sql("UPDATE LearningActivities SET Instructions = 'Read each displayed word aloud and choose how the practice try went. Speech is not recorded or evaluated.' WHERE Id = 3;");
    }
}
