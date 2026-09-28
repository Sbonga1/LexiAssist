using System.Reflection;
using System.Text;
using LexiAssist.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LexiAssist.Web.Data.Migrations.AzureSql;

[DbContext(typeof(AzureSqlApplicationDbContext))]
[Migration("20260928000100_AzureSqlInitial")]
partial class AzureSqlInitial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ReadScript("AzureSqlInitial.Up.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(ReadScript("AzureSqlInitial.Down.sql"));

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        AzureSqlApplicationDbContextModelSnapshot.BuildModelForMigration(modelBuilder);

    private static string ReadScript(string fileName)
    {
        var resourceName = $"LexiAssist.Web.Data.Migrations.AzureSql.{fileName}";
        using var stream = typeof(AzureSqlInitial).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded Azure SQL migration script '{resourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
