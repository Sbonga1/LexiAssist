using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

public partial class AddSayWriteMatchActivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "LearningActivities",
            columns: new[] { "Id", "ActivityType", "Description", "Instructions", "IsActive", "Name" },
            values: new object[]
            {
                5,
                "Spelling",
                "Practise matching letter chunks to build a spelled word.",
                "Listen to pronunciation audio when available, read the clue, and arrange the chunks to build each word.",
                true,
                "Say-Write-Match"
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "LearningActivities",
            keyColumn: "Id",
            keyValue: 5);
    }
}
