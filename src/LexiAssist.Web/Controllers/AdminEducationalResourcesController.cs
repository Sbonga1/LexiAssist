using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Resources;
using LexiAssist.Web.ViewModels.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/resources")]
public sealed class AdminEducationalResourcesController(ApplicationDbContext dbContext) : Controller
{
    private const int PageSize = 5;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        string? type,
        string? status,
        int? page,
        CancellationToken cancellationToken = default)
    {
        var searchFilter = NormalizeFilter(search, 200);
        var resourceTypeFilter = NormalizeFilter(type, 64);
        var statusFilter = NormalizeStatus(status);
        var resourcesQuery = dbContext.EducationalResources.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            resourcesQuery = resourcesQuery.Where(resource =>
                resource.Title.Contains(searchFilter) || resource.Summary.Contains(searchFilter));
        }

        if (!string.IsNullOrWhiteSpace(resourceTypeFilter))
        {
            resourcesQuery = resourcesQuery.Where(resource => resource.ResourceType == resourceTypeFilter);
        }

        if (statusFilter.HasValue)
        {
            resourcesQuery = resourcesQuery.Where(resource => resource.IsPublished == statusFilter.Value);
        }

        var totalItems = await resourcesQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)PageSize));
        var currentPage = Math.Clamp(page.GetValueOrDefault(1), 1, totalPages);
        var resources = await resourcesQuery
            .OrderByDescending(resource => resource.UpdatedAtUtc)
            .ThenBy(resource => resource.Title)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(resource => new EducationalResourceListItemViewModel(
                resource.Id,
                resource.Title,
                resource.Summary,
                resource.ResourceType,
                resource.IsPublished,
                resource.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var resourceTypes = await dbContext.EducationalResources
            .AsNoTracking()
            .Select(resource => resource.ResourceType)
            .Where(resourceType => resourceType != "")
            .Distinct()
            .OrderBy(resourceType => resourceType)
            .ToListAsync(cancellationToken);

        return View(new EducationalResourcesIndexViewModel(resources, true)
        {
            Search = searchFilter,
            ResourceTypeFilter = resourceTypeFilter,
            StatusFilter = statusFilter.HasValue
                ? (statusFilter.Value ? "published" : "draft")
                : null,
            ResourceTypes = resourceTypes,
            CurrentPage = currentPage,
            PageSize = PageSize,
            TotalItems = totalItems
        });
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new EducationalResourceFormViewModel());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var resource = await dbContext.EducationalResources
            .AsNoTracking()
            .Where(resource => resource.Id == id)
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

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EducationalResourceFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var now = DateTime.UtcNow;
        var resource = new EducationalResource
        {
            Title = model.Title.Trim(),
            Summary = model.Summary.Trim(),
            Content = model.Content.Trim(),
            ResourceType = model.ResourceType.Trim(),
            ExternalUrl = NormalizeUrl(model.ExternalUrl),
            IsPublished = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.EducationalResources.Add(resource);
        await dbContext.SaveChangesAsync();

        TempData["StatusMessage"] = "The educational resource was created as a draft.";
        return RedirectToAction(nameof(Edit), new { id = resource.Id });
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var resource = await dbContext.EducationalResources
            .AsNoTracking()
            .SingleOrDefaultAsync(resource => resource.Id == id);

        return resource is null ? NotFound() : View(ToFormViewModel(resource));
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EducationalResourceFormViewModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var resource = await dbContext.EducationalResources.FindAsync(id);
        if (resource is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        resource.Title = model.Title.Trim();
        resource.Summary = model.Summary.Trim();
        resource.Content = model.Content.Trim();
        resource.ResourceType = model.ResourceType.Trim();
        resource.ExternalUrl = NormalizeUrl(model.ExternalUrl);
        resource.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        TempData["StatusMessage"] = "The educational resource was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/publish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Publish(int id) => SetPublishedAsync(id, true);

    [HttpPost("{id:int}/unpublish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Unpublish(int id) => SetPublishedAsync(id, false);

    private async Task<IActionResult> SetPublishedAsync(int id, bool isPublished)
    {
        var resource = await dbContext.EducationalResources.FindAsync(id);
        if (resource is null)
        {
            return NotFound();
        }

        resource.IsPublished = isPublished;
        resource.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        TempData["StatusMessage"] = isPublished
            ? "The educational resource is now published."
            : "The educational resource is now unpublished.";
        return RedirectToAction(nameof(Index));
    }

    private static EducationalResourceFormViewModel ToFormViewModel(EducationalResource resource) => new()
    {
        Id = resource.Id,
        Title = resource.Title,
        Summary = resource.Summary,
        Content = resource.Content,
        ResourceType = resource.ResourceType,
        ExternalUrl = resource.ExternalUrl
    };

    private static string? NormalizeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    private static string? NormalizeFilter(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (normalized?.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool? NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "draft" => false,
        "published" => true,
        _ => null
    };
}
