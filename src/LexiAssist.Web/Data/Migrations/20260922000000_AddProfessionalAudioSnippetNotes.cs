using LexiAssist.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922000000_AddProfessionalAudioSnippetNotes")]
public partial class AddProfessionalAudioSnippetNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProfessionalNote",
            table: "LearnerAudioRecordings",
            type: "TEXT",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProfessionalNoteUpdatedAtUtc",
            table: "LearnerAudioRecordings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProfessionalAudioNote",
            table: "ScreeningResponses",
            type: "TEXT",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProfessionalAudioNoteUpdatedAtUtc",
            table: "ScreeningResponses",
            type: "TEXT",
            nullable: true);

    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProfessionalNote", table: "LearnerAudioRecordings");
        migrationBuilder.DropColumn(name: "ProfessionalNoteUpdatedAtUtc", table: "LearnerAudioRecordings");
        migrationBuilder.DropColumn(name: "ProfessionalAudioNote", table: "ScreeningResponses");
        migrationBuilder.DropColumn(name: "ProfessionalAudioNoteUpdatedAtUtc", table: "ScreeningResponses");
    }
}
