using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreeningSkillAreaResultDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasFlaggedSkillArea",
                table: "ScreeningTestAttempts",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasFlaggedSkillArea",
                table: "ScreeningTestAttempts");
        }
    }
}
