using LexiAssist.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Data;

public sealed class DatabaseInitializationService(
    ApplicationDbContext dbContext,
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (environment.IsDevelopment())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        await EnsureRolesAsync();

        if (environment.IsDevelopment())
        {
            await SeedDevelopmentAdministratorAsync();
        }

        if (environment.IsProduction())
        {
            await BootstrapProductionAdministratorAsync();
        }
    }

    private async Task EnsureRolesAsync()
    {
        var roleNames = new[]
        {
            ApplicationRoles.Learner,
            ApplicationRoles.Administrator,
            ApplicationRoles.Psychologist
        };

        foreach (var roleName in roleNames)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            EnsureSucceeded(result, $"create role '{roleName}'");
        }
    }

    private async Task SeedDevelopmentAdministratorAsync()
    {
        if (!configuration.GetValue<bool>("DevelopmentAdminSeed:Enabled"))
        {
            return;
        }

        var email = configuration["DevelopmentAdminSeed:Email"];
        var password = configuration["DevelopmentAdminSeed:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Development administrator seeding is enabled, but DevelopmentAdminSeed:Email or " +
                "DevelopmentAdminSeed:Password is missing.");
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "create the development administrator");
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            var addRoleResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Administrator);
            EnsureSucceeded(addRoleResult, "assign the Administrator role to the development administrator");
        }
    }

    private async Task BootstrapProductionAdministratorAsync()
    {
        if (!configuration.GetValue<bool>("ProductionAdminBootstrap:Enabled"))
        {
            return;
        }

        var email = configuration["ProductionAdminBootstrap:Email"];
        var password = configuration["ProductionAdminBootstrap:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "ProductionAdminBootstrap is enabled, but ProductionAdminBootstrap:Email or " +
                "ProductionAdminBootstrap:Password is missing.");
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            EnsureSucceeded(createResult, "create the production administrator");
        }

        if (!await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            var addRoleResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Administrator);
            EnsureSucceeded(addRoleResult, "assign the Administrator role to the production bootstrap account");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Could not {operation}: {errors}");
    }
}
