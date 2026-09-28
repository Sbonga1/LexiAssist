using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.ViewModels.AdminScreeningTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/screening-tests")]
public sealed class AdminScreeningTestsController(
    ApplicationDbContext dbContext,
    IAuditService auditService,
    IAssessmentReadinessService readinessService) : Controller
{
    private const int IndexPageSize = 6;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int? page)
    {
        search = search?.Trim() ?? string.Empty;
        var query = dbContext.ScreeningTests
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(test =>
                EF.Functions.Like(test.Name, pattern) ||
                EF.Functions.Like(test.Slug, pattern));
        }

        var totalItems = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)IndexPageSize));
        var currentPage = Math.Clamp(page.GetValueOrDefault(1), 1, totalPages);
        var tests = await query
            .OrderBy(test => test.SortOrder)
            .ThenBy(test => test.Id)
            .Skip((currentPage - 1) * IndexPageSize)
            .Take(IndexPageSize)
            .Select(test => new AdminScreeningTestListItemViewModel(
                test.Id,
                test.Name,
                test.Slug,
                test.SortOrder,
                test.IsActive))
            .ToListAsync();

        return View(new AdminScreeningTestsIndexViewModel(
            tests,
            search,
            currentPage,
            IndexPageSize,
            totalItems,
            totalPages));
    }

    [HttpPost("reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(int id, int? beforeId)
    {
        var tests = await dbContext.ScreeningTests
            .OrderBy(test => test.SortOrder)
            .ThenBy(test => test.Id)
            .ToListAsync();
        var currentIndex = tests.FindIndex(test => test.Id == id);
        if (currentIndex < 0)
        {
            return NotFound();
        }

        if (beforeId == id)
        {
            return BadRequest();
        }

        var screeningTest = tests[currentIndex];
        tests.RemoveAt(currentIndex);

        var destinationIndex = beforeId.HasValue
            ? tests.FindIndex(test => test.Id == beforeId.Value)
            : tests.Count;
        if (destinationIndex < 0)
        {
            return NotFound();
        }

        tests.Insert(destinationIndex, screeningTest);
        for (var index = 0; index < tests.Count; index++)
        {
            tests[index].SortOrder = index + 1;
        }

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestReordered, AuditEntityTypes.ScreeningTest, id.ToString(), true,
            new { Operation = "Drag", BeforeId = beforeId });
        TempData["StatusMessage"] = "The screening test order was updated.";

        var targetPage = (destinationIndex / IndexPageSize) + 1;
        return RedirectToAction(nameof(Index), new { page = targetPage });
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new AdminScreeningTestFormViewModel());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminScreeningTestFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var slug = NormalizeSlug(model.Slug);
        if (await SlugExistsAsync(slug))
        {
            ModelState.AddModelError(nameof(model.Slug), "A screening test with this slug already exists.");
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.ScreeningTest, null, false,
                new { Operation = "Create", Reason = "DuplicateSlug" });
            return View(model);
        }

        var sortOrder = (await dbContext.ScreeningTests.MaxAsync(test => (int?)test.SortOrder) ?? 0) + 1;
        var screeningTest = new ScreeningTest
        {
            Name = model.Name.Trim(),
            Slug = slug,
            Description = model.Description.Trim(),
            Instructions = model.Instructions.Trim(),
            IsActive = false,
            SortOrder = sortOrder
        };

        dbContext.ScreeningTests.Add(screeningTest);
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestCreated, AuditEntityTypes.ScreeningTest, screeningTest.Id.ToString(), true,
            new { screeningTest.Slug });

        TempData["StatusMessage"] = "The screening test was created.";
        return RedirectToAction(nameof(Details), new { id = screeningTest.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var screeningTest = await dbContext.ScreeningTests
            .AsNoTracking()
            .Where(test => test.Id == id)
            .Select(test => new AdminScreeningTestDetailsViewModel(
                test.Id,
                test.Name,
                test.Slug,
                test.Description,
                test.Instructions,
                test.SortOrder,
                test.IsActive,
                test.Questions.Count))
            .SingleOrDefaultAsync();

        return screeningTest is null ? NotFound() : View(screeningTest);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var screeningTest = await dbContext.ScreeningTests
            .AsNoTracking()
            .SingleOrDefaultAsync(test => test.Id == id);

        return screeningTest is null
            ? NotFound()
            : View(ToFormViewModel(screeningTest));
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminScreeningTestFormViewModel model, bool returnToBuilder = false)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        var screeningTest = await dbContext.ScreeningTests.FindAsync(id);
        if (screeningTest is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return returnToBuilder
                ? View("~/Views/AdminAssessmentBuilder/Details.cshtml", model)
                : View(model);
        }

        var slug = NormalizeSlug(model.Slug);
        if (await SlugExistsAsync(slug, screeningTest.Id))
        {
            ModelState.AddModelError(nameof(model.Slug), "A screening test with this slug already exists.");
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.ScreeningTest, screeningTest.Id.ToString(), false,
                new { Operation = "Edit", Reason = "DuplicateSlug" });
            return returnToBuilder
                ? View("~/Views/AdminAssessmentBuilder/Details.cshtml", model)
                : View(model);
        }

        screeningTest.Name = model.Name.Trim();
        screeningTest.Slug = slug;
        screeningTest.Description = model.Description.Trim();
        screeningTest.Instructions = model.Instructions.Trim();
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestUpdated, AuditEntityTypes.ScreeningTest, screeningTest.Id.ToString(), true,
            new { screeningTest.Slug });

        TempData["StatusMessage"] = "The screening test was updated.";
        return returnToBuilder
            ? RedirectToAction("Details", "AdminAssessmentBuilder", new { testId = screeningTest.Id })
            : RedirectToAction(nameof(Details), new { id = screeningTest.Id });
    }

    [HttpPost("{id:int}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id)
    {
        var screeningTest = await dbContext.ScreeningTests.FindAsync(id);
        if (screeningTest is null)
        {
            return NotFound();
        }

        var readiness = await readinessService.GetReadinessAsync(id);
        if (readiness is null)
        {
            return NotFound();
        }
        if (!readiness.IsReady)
        {
            TempData["ErrorMessage"] = readiness.ActivationFeedback;
            return RedirectToAction(nameof(Details), new { id });
        }

        screeningTest.IsActive = true;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestActivated, AuditEntityTypes.ScreeningTest, id.ToString(), true);
        TempData["StatusMessage"] = "The screening test was activated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        var screeningTest = await dbContext.ScreeningTests.FindAsync(id);
        if (screeningTest is null)
        {
            return NotFound();
        }

        screeningTest.IsActive = false;
        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ScreeningTestDeactivated, AuditEntityTypes.ScreeningTest, id.ToString(), true);
        TempData["StatusMessage"] = "The screening test was deactivated. Its record and any related data are preserved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, string direction, string? search = null)
    {
        if (direction is not "up" and not "down")
        {
            return BadRequest();
        }

        var tests = await dbContext.ScreeningTests
            .OrderBy(test => test.SortOrder)
            .ThenBy(test => test.Id)
            .ToListAsync();
        var currentIndex = tests.FindIndex(test => test.Id == id);
        if (currentIndex < 0)
        {
            return NotFound();
        }

        var destinationIndex = direction == "up" ? currentIndex - 1 : currentIndex + 1;
        if (destinationIndex >= 0 && destinationIndex < tests.Count)
        {
            var screeningTest = tests[currentIndex];
            tests.RemoveAt(currentIndex);
            tests.Insert(destinationIndex, screeningTest);

            for (var index = 0; index < tests.Count; index++)
            {
                tests[index].SortOrder = index + 1;
            }

            await dbContext.SaveChangesAsync();
            await auditService.RecordAsync(AuditActions.ScreeningTestReordered, AuditEntityTypes.ScreeningTest, id.ToString(), true,
                new { Direction = direction });
            TempData["StatusMessage"] = "The screening test order was updated.";
        }

        var targetPage = (destinationIndex / IndexPageSize) + 1;
        return RedirectToAction(nameof(Index), new { page = targetPage });
    }

    private Task<bool> SlugExistsAsync(string slug, int? exceptId = null) =>
        dbContext.ScreeningTests.AnyAsync(test =>
            test.Slug == slug && (!exceptId.HasValue || test.Id != exceptId.Value));

    private static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

    private static AdminScreeningTestFormViewModel ToFormViewModel(ScreeningTest screeningTest) => new()
    {
        Id = screeningTest.Id,
        Name = screeningTest.Name,
        Slug = screeningTest.Slug,
        Description = screeningTest.Description,
        Instructions = screeningTest.Instructions
    };
}
