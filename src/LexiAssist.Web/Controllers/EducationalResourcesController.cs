using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.ViewModels.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Learner + "," + ApplicationRoles.Psychologist)]
[Route("resources")]
public sealed class EducationalResourcesController(ApplicationDbContext dbContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var resources = await dbContext.EducationalResources
            .AsNoTracking()
            .Where(resource => resource.IsPublished)
            .OrderBy(resource => resource.Title)
            .Select(resource => new EducationalResourceListItemViewModel(
                resource.Id,
                resource.Title,
                resource.Summary,
                resource.ResourceType,
                resource.IsPublished,
                resource.UpdatedAtUtc))
            .ToListAsync();

        return View(new EducationalResourcesIndexViewModel(resources, false));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var resource = await dbContext.EducationalResources
            .AsNoTracking()
            .Where(resource => resource.Id == id && resource.IsPublished)
            .Select(resource => new EducationalResourceDetailsViewModel(
                resource.Id,
                resource.Title,
                resource.Summary,
                resource.Content,
                resource.ResourceType,
                resource.ExternalUrl,
                resource.IsPublished,
                resource.CreatedAtUtc,
                resource.UpdatedAtUtc))
            .SingleOrDefaultAsync();

        return resource is null ? NotFound() : View(resource);
    }
}
