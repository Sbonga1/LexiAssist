using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations;

public partial class AddFastNameRaceActivity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "LearningActivities",
            columns: new[] { "Id", "ActivityType", "Description", "Instructions", "IsActive", "Name" },
            values: new object[]
            {
                2,
                "Fluency",
                "Practise naming a changing grid of familiar items at a comfortable, steady pace.",
                "Name each highlighted item, mark an item if you want to note a correction, and pause whenever you need.",
                true,
                "Fast Name Race"
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DeleteData(
            table: "LearningActivities",
            keyColumn: "Id",
            keyValue: 2);
    }
}
