using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

public partial class AddWordReaderActivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "LearningActivities",
            columns: new[] { "Id", "ActivityType", "Description", "Instructions", "IsActive", "Name" },
            values: new object[]
            {
                3,
                "Reading",
                "Practise reading words selected for a screening support area.",
                "Read each displayed word aloud and choose how the practice try went. Speech is not recorded or evaluated.",
                true,
                "Word Reader"
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "LearningActivities",
            keyColumn: "Id",
            keyValue: 3);
    }
}
