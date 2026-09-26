using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreeningResponseErrorCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScreeningResponseErrorCategories",
                columns: table => new
                {
                    ScreeningResponseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningResponseErrorCategories", x => new { x.ScreeningResponseId, x.Category });
                    table.ForeignKey(
                        name: "FK_ScreeningResponseErrorCategories_ScreeningResponses_ScreeningResponseId",
                        column: x => x.ScreeningResponseId,
                        principalTable: "ScreeningResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScreeningResponseErrorCategories");
        }
    }
}
