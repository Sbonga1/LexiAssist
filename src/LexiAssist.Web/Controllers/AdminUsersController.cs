using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.AdminUsers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/users")]
public sealed class AdminUsersController(UserManager<ApplicationUser> userManager, IAuditService auditService) : Controller
{
    private const int PageSize = 20;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var searchTerm = search?.Trim();
        if (searchTerm?.Length > 100)
        {
            searchTerm = searchTerm[..100];
        }

        var users = userManager.Users;

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            users = users.Where(user =>
                (user.Email != null && user.Email.Contains(searchTerm)) ||
                user.FirstName.Contains(searchTerm) ||
                user.LastName.Contains(searchTerm) ||
                (user.PreferredName != null && user.PreferredName.Contains(searchTerm)));
        }

        var totalCount = await users.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var pageNumber = Math.Clamp(page, 1, totalPages);

        var pageUsers = await users
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .ThenBy(user => user.Email)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var rows = new List<AdminUserListItemViewModel>(pageUsers.Count);
        foreach (var user in pageUsers)
        {
            var roles = await userManager.GetRolesAsync(user);
            rows.Add(new AdminUserListItemViewModel(
                user.Id,
                user.FirstName,
                user.LastName,
                user.PreferredName,
                user.Email ?? string.Empty,
                user.IsActive,
                string.Join(", ", roles)));
        }

        return View(new AdminUsersIndexViewModel
        {
            Users = rows,
            SearchTerm = searchTerm,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    [HttpGet("create")]
    public IActionResult Create() => View(new AdminUserCreateViewModel());

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminUserCreateViewModel model)
    {
        if (model.Role is not ManagedUserRole.Learner and not ManagedUserRole.Psychologist)
        {
            ModelState.AddModelError(nameof(model.Role), "Choose Learner or Psychologist.");
        }

        if (model.DateOfBirth is { } dateOfBirth && dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(model.DateOfBirth), "Date of birth cannot be in the future.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email address already exists.");
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, null, false,
                new { Operation = "Create", Reason = "DuplicateEmail" });
            return View(model);
        }

        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            PreferredName = string.IsNullOrWhiteSpace(model.PreferredName) ? null : model.PreferredName.Trim(),
            DateOfBirth = model.DateOfBirth,
            IsActive = true,
            CreatedAtUtc = now
        };

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                new { Operation = "Create", Reason = "IdentityValidation" });
            AddIdentityErrors(createResult.Errors);
            return View(model);
        }

        var role = model.Role == ManagedUserRole.Psychologist
            ? ApplicationRoles.Psychologist
            : ApplicationRoles.Learner;
        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            user.IsActive = false;
            await userManager.UpdateAsync(user);
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                new { Operation = "AssignRole", Reason = "IdentityFailure", Role = role });
            ModelState.AddModelError(string.Empty, "The account could not be assigned its role. Please try again.");
            return View(model);
        }

        await auditService.RecordAsync(AuditActions.UserCreated, AuditEntityTypes.User, user.Id, true,
            new { Role = role });
        await auditService.RecordAsync(AuditActions.RoleChanged, AuditEntityTypes.User, user.Id, true,
            new { Role = role, Operation = "AssignedOnCreate" });

        TempData["StatusMessage"] = $"The {role} account was created.";
        return RedirectToAction(nameof(Details), new { id = user.Id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Details(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        return View(await ToDetailsViewModelAsync(user));
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        return View(ToEditViewModel(user));
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, AdminUserEditViewModel model)
    {
        if (!string.Equals(id, model.Id, StringComparison.Ordinal))
        {
            return BadRequest();
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (model.DateOfBirth is { } dateOfBirth && dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(model.DateOfBirth), "Date of birth cannot be in the future.");
        }

        var email = model.Email.Trim();
        var existingEmailUser = await userManager.FindByEmailAsync(email);
        if (existingEmailUser is not null && existingEmailUser.Id != user.Id)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email address already exists.");
        }

        if (!ModelState.IsValid)
        {
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                new { Operation = "Edit", Reason = "Validation" });
            return View(model);
        }

        user.Email = email;
        user.UserName = email;
        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.PreferredName = string.IsNullOrWhiteSpace(model.PreferredName) ? null : model.PreferredName.Trim();
        user.DateOfBirth = model.DateOfBirth;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                new { Operation = "Edit", Reason = "IdentityFailure" });
            AddIdentityErrors(updateResult.Errors);
            return View(model);
        }

        await auditService.RecordAsync(AuditActions.UserProfileUpdated, AuditEntityTypes.User, user.Id, true,
            new { Operation = "ProfileUpdated" });

        TempData["StatusMessage"] = "The user profile was updated.";
        return RedirectToAction(nameof(Details), new { id = user.Id });
    }

    [HttpPost("{id}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (string.Equals(user.Id, userManager.GetUserId(User), StringComparison.Ordinal))
        {
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                new { Operation = "Deactivate", Reason = "CurrentAdministrator" });
            TempData["ErrorMessage"] = "You cannot deactivate the administrator account you are currently using.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (user.IsActive)
        {
            user.IsActive = false;
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                    new { Operation = "Deactivate", Reason = "IdentityFailure" });
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(Details), new { id });
            }

            await userManager.UpdateSecurityStampAsync(user);
            await auditService.RecordAsync(AuditActions.UserDeactivated, AuditEntityTypes.User, user.Id, true);
        }

        TempData["StatusMessage"] = "The user account was deactivated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (!user.IsActive)
        {
            user.IsActive = true;
            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                    new { Operation = "Activate", Reason = "IdentityFailure" });
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(Details), new { id });
            }

            await userManager.UpdateSecurityStampAsync(user);
            await auditService.RecordAsync(AuditActions.UserActivated, AuditEntityTypes.User, user.Id, true);
        }

        TempData["StatusMessage"] = "The user account was activated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<AdminUserDetailsViewModel> ToDetailsViewModelAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var orderedUserIds = await userManager.Users
            .OrderBy(candidate => candidate.LastName)
            .ThenBy(candidate => candidate.FirstName)
            .ThenBy(candidate => candidate.Email)
            .Select(candidate => candidate.Id)
            .ToListAsync();
        var currentIndex = orderedUserIds.FindIndex(id => string.Equals(id, user.Id, StringComparison.Ordinal));

        return new AdminUserDetailsViewModel
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PreferredName = user.PreferredName,
            DateOfBirth = user.DateOfBirth,
            Email = user.Email ?? string.Empty,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc,
            ScreeningPrivacyNoticeAcceptedAtUtc = user.ScreeningPrivacyNoticeAcceptedAtUtc,
            Roles = roles.ToArray(),
            IsCurrentUser = string.Equals(user.Id, userManager.GetUserId(User), StringComparison.Ordinal),
            CurrentPosition = currentIndex >= 0 ? currentIndex + 1 : 1,
            TotalUsers = orderedUserIds.Count,
            PreviousUserId = currentIndex > 0 ? orderedUserIds[currentIndex - 1] : null,
            NextUserId = currentIndex >= 0 && currentIndex < orderedUserIds.Count - 1
                ? orderedUserIds[currentIndex + 1]
                : null
        };
    }

    private static AdminUserEditViewModel ToEditViewModel(ApplicationUser user) => new()
    {
        Id = user.Id,
        FirstName = user.FirstName,
        LastName = user.LastName,
        PreferredName = user.PreferredName,
        DateOfBirth = user.DateOfBirth,
        Email = user.Email ?? string.Empty
    };

    private void AddIdentityErrors(IEnumerable<IdentityError> errors)
    {
        foreach (var error in errors)
        {
            var field = error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" => "Email",
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => "Password",
                _ => string.Empty
            };

            ModelState.AddModelError(field, error.Description);
        }
    }
}
