using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningActivityDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearningActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActivityType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LearnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    LearningActivityId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RelatedScreeningReportId = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_AspNetUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_AspNetUsers_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_LearningActivities_LearningActivityId",
                        column: x => x.LearningActivityId,
                        principalTable: "LearningActivities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_ScreeningReports_RelatedScreeningReportId",
                        column: x => x.RelatedScreeningReportId,
                        principalTable: "ScreeningReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActivityAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityAssignmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Score = table.Column<double>(type: "REAL", nullable: true),
                    Accuracy = table.Column<double>(type: "REAL", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAttempts_ActivityAssignments_ActivityAssignmentId",
                        column: x => x.ActivityAssignmentId,
                        principalTable: "ActivityAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_AssignedByUserId",
                table: "ActivityAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_LearnerId_Status_AssignedAtUtc",
                table: "ActivityAssignments",
                columns: new[] { "LearnerId", "Status", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_LearningActivityId",
                table: "ActivityAssignments",
                column: "LearningActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_RelatedScreeningReportId",
                table: "ActivityAssignments",
                column: "RelatedScreeningReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAttempts_ActivityAssignmentId_StartedAtUtc",
                table: "ActivityAttempts",
                columns: new[] { "ActivityAssignmentId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningActivities_IsActive_ActivityType",
                table: "LearningActivities",
                columns: new[] { "IsActive", "ActivityType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityAttempts");

            migrationBuilder.DropTable(
                name: "ActivityAssignments");

            migrationBuilder.DropTable(
                name: "LearningActivities");
        }
    }
}
