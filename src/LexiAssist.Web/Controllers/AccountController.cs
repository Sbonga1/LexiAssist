using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LexiAssist.Web.Controllers;

public sealed class AccountController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuditService auditService) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }

        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboard();
        }

        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!model.AcceptScreeningPrivacyNotice)
        {
            ModelState.AddModelError(
                nameof(model.AcceptScreeningPrivacyNotice),
                "You must acknowledge the screening and privacy notice to create an account.");
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
            return View(model);
        }

        var noticeAcceptedAtUtc = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            PreferredName = string.IsNullOrWhiteSpace(model.PreferredName) ? null : model.PreferredName.Trim(),
            DateOfBirth = model.DateOfBirth,
            IsActive = true,
            CreatedAtUtc = noticeAcceptedAtUtc,
            ScreeningPrivacyNoticeAcceptedAtUtc = noticeAcceptedAtUtc
        };

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                var field = error.Code switch
                {
                    "DuplicateEmail" or "DuplicateUserName" => nameof(model.Email),
                    _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => nameof(model.Password),
                    _ => string.Empty
                };

                ModelState.AddModelError(field, error.Description);
            }

            return View(model);
        }

        var learnerRoleResult = await userManager.AddToRoleAsync(user, ApplicationRoles.Learner);
        if (!learnerRoleResult.Succeeded)
        {
            user.IsActive = false;
            await userManager.UpdateAsync(user);
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.User, user.Id, false,
                new { Operation = "PublicRegistrationRoleAssignment", Role = ApplicationRoles.Learner });
            ModelState.AddModelError(string.Empty, "We could not complete your registration. Please try again.");
            return View(model);
        }

        await auditService.RecordAsync(AuditActions.UserCreated, AuditEntityTypes.User, user.Id, true,
            new { Source = "PublicRegistration" });
        await auditService.RecordAsync(AuditActions.RoleChanged, AuditEntityTypes.User, user.Id, true,
            new { Role = ApplicationRoles.Learner, Source = "PublicRegistration" });

        if (await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Learner", "Dashboard");
        }

        TempData["StatusMessage"] =
            "Your learner account was created. Sign-in requires a confirmed email; contact your LexiAssist administrator for help.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || string.IsNullOrWhiteSpace(user.UserName))
        {
            ModelState.AddModelError(string.Empty, "The email address or password is incorrect.");
            return View(model);
        }

        var result = await signInManager.PasswordSignInAsync(
            user.UserName,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return await RedirectToDashboardAsync(user);
        }

        var message = result.IsLockedOut
            ? "This account is temporarily locked. Please try again later."
            : "The email address or password is incorrect.";
        ModelState.AddModelError(string.Empty, message);
        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        TempData["StatusMessage"] = "You have been signed out.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectToDashboard()
    {
        if (User.IsInRole(ApplicationRoles.Administrator))
        {
            return RedirectToAction("Administrator", "Dashboard");
        }

        if (User.IsInRole(ApplicationRoles.Psychologist))
        {
            return RedirectToAction("Psychologist", "Dashboard");
        }

        if (User.IsInRole(ApplicationRoles.Learner))
        {
            return RedirectToAction("Learner", "Dashboard");
        }

        return RedirectToAction(nameof(AccessDenied));
    }

    private async Task<IActionResult> RedirectToDashboardAsync(ApplicationUser user)
    {
        if (await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            return RedirectToAction("Administrator", "Dashboard");
        }

        if (await userManager.IsInRoleAsync(user, ApplicationRoles.Psychologist))
        {
            return RedirectToAction("Psychologist", "Dashboard");
        }

        if (await userManager.IsInRoleAsync(user, ApplicationRoles.Learner))
        {
            return RedirectToAction("Learner", "Dashboard");
        }

        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(AccessDenied));
    }
}
