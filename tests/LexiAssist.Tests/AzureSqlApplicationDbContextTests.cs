using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace LexiAssist.Tests;

public sealed class AzureSqlApplicationDbContextTests
{
    [Fact]
    public void AzureSqlContext_uses_its_own_migration_and_provider_types()
    {
        var options = new DbContextOptionsBuilder<AzureSqlApplicationDbContext>()
            .UseSqlServer("Server=(local);Database=LexiAssist;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var context = new AzureSqlApplicationDbContext(options);
        var migrations = context.Database.GetMigrations().ToArray();
        var createScript = context.Database.GenerateCreateScript();
        var reminderTime = context.Model.FindEntityType(typeof(LearnerReminderPreference))!
            .FindProperty(nameof(LearnerReminderPreference.PreferredReminderTime))!;

        Assert.Equal(["20260928000100_AzureSqlInitial"], migrations);
        Assert.Equal("time", reminderTime.GetColumnType());
        Assert.Contains("[PreferredReminderTime] time NOT NULL", createScript, StringComparison.Ordinal);
        Assert.Contains("WHERE [Status] = 1", createScript, StringComparison.Ordinal);
        Assert.Contains("CK_LearnerAudioRecordings_OneTarget] CHECK (([ActivityAttemptId]", createScript, StringComparison.Ordinal);

        var migrationType = context.GetService<IMigrationsAssembly>().Migrations.Single().Value;
        var initialMigration = context.GetService<IMigrationsAssembly>()
            .CreateMigration(migrationType, "Microsoft.EntityFrameworkCore.SqlServer");
        var upScript = Assert.IsType<SqlOperation>(Assert.Single(initialMigration.UpOperations)).Sql;
        var downScript = Assert.IsType<SqlOperation>(Assert.Single(initialMigration.DownOperations)).Sql;
        Assert.Contains("CREATE TABLE [AspNetUsers]", upScript, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [AssessmentDrafts]", upScript, StringComparison.Ordinal);
        Assert.Contains("DROP TABLE [AspNetUsers]", downScript, StringComparison.Ordinal);
    }
}
