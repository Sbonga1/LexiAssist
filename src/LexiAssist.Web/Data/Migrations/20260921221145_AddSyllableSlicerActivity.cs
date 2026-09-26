using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

public partial class AddSyllableSlicerActivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "LearningActivities",
            columns: new[] { "Id", "ActivityType", "Description", "Instructions", "IsActive", "Name" },
            values: new object[]
            {
                4,
                "Reading",
                "Practise arranging syllable chunks to build longer words.",
                "Look at the word, arrange its syllable chunks, then check your answer at a comfortable pace.",
                true,
                "Syllable Slicer"
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "LearningActivities",
            keyColumn: "Id",
            keyValue: 4);
    }
}
