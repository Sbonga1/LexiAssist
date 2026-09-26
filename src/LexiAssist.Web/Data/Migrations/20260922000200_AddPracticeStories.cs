using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeStories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PracticeStories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LearnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TargetPhoneticPattern = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TargetWordsJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DifficultyLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    StoryText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    GenerationMethod = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeStories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeStories_AspNetUsers_LearnerId",
                        column: x => x.LearnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeStories_LearnerId_GeneratedAtUtc",
                table: "PracticeStories",
                columns: new[] { "LearnerId", "GeneratedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PracticeStories");
        }
    }
}
