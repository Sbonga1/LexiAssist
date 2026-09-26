using Microsoft.Extensions.Diagnostics.HealthChecks;
using LexiAssist.Web.Data;

namespace LexiAssist.Web.Services;

public sealed class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database connectivity check failed.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Database connectivity check failed.");
        }
    }
}
