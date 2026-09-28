using LexiAssist.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LexiAssist.Tests;

public sealed class LearnerAudioRecordingModelTests
{
    [Fact]
    public void Migration_snapshot_matches_current_ef_model()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=LexiAssistModelDesign;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var context = new ApplicationDbContext(options);
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var differ = context.GetService<IMigrationsModelDiffer>();
        var initializer = context.GetService<IModelRuntimeInitializer>();
        var snapshotModel = initializer.Initialize(snapshot.Model, designTime: true);
        var current = context.GetService<IDesignTimeModel>().Model;
        var differences = differ.GetDifferences(snapshotModel.GetRelationalModel(), current.GetRelationalModel());
        Assert.Empty(differences);
    }
}
