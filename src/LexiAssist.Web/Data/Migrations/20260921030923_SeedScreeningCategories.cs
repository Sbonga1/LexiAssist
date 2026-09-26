using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LexiAssist.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedScreeningCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ScreeningTests",
                columns: new[] { "Id", "Description", "Instructions", "IsActive", "Name", "Slug", "SortOrder" },
                values: new object[,]
                {
                    { 1, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Word Reading", "word-reading", 1 },
                    { 2, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Decoding / Non-word Reading", "decoding-non-word-reading", 2 },
                    { 3, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Phonological Awareness", "phonological-awareness", 3 },
                    { 4, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Rapid Naming", "rapid-naming", 4 },
                    { 5, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Spelling", "spelling", 5 },
                    { 6, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Reading Fluency", "reading-fluency", 6 },
                    { 7, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Reading Comprehension", "reading-comprehension", 7 },
                    { 8, "Category for future educational screening and support content.", "No screening items are configured yet.", true, "Oral Language and Listening Comprehension", "oral-language-listening-comprehension", 8 },
                    { 9, "Optional category. Disabled pending stakeholder and professional confirmation.", "No screening items are configured yet.", false, "Memory and Broader Thinking Skills", "memory-broader-thinking-skills", 9 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "ScreeningTests",
                keyColumn: "Id",
                keyValue: 9);
        }
    }
}
