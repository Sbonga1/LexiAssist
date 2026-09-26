using System.Security.Claims;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.Controllers;

[Authorize]
public sealed class SettingsController(
    IUserAccessibilityPreferenceService preferenceService,
    IUserInterestService interestService,
    IUserReminderPreferenceService reminderPreferenceService,
    IOptions<LearnerInterestOptions> configuredInterestOptions) : Controller
{
    private readonly LearnerInterestOptions interestOptions = configuredInterestOptions.Value;

    [HttpGet]
    public async Task<IActionResult> Accessibility(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var preference = await preferenceService.GetForUserAsync(userId, cancellationToken);
        var selectedInterests = await interestService.GetSelectedKeysAsync(userId, cancellationToken);
        var reminderPreference = await reminderPreferenceService.GetForUserAsync(userId, cancellationToken);
        return View(AccessibilityPreferencesViewModel.FromPreference(
            preference,
            interestOptions.Available,
            selectedInterests,
            reminderPreference));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accessibility(
        AccessibilityPreferencesViewModel model,
        CancellationToken cancellationToken)
    {
        model.AvailableInterests = interestOptions.Available;
        model.SelectedInterests ??= [];
        model.SelectedInterests = model.SelectedInterests
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var availableKeys = interestOptions.Available
            .Select(option => option.Key.Trim().ToLowerInvariant())
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (model.SelectedInterests.Any(key => !availableKeys.Contains(key)))
        {
            ModelState.AddModelError(nameof(model.SelectedInterests), "Choose interests from the available options.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = GetCurrentUserId();
        await preferenceService.SaveForUserAsync(model.ToPreference(userId), cancellationToken);
        await interestService.SaveSelectedKeysAsync(userId, model.SelectedInterests, cancellationToken);
        await reminderPreferenceService.SaveForUserAsync(model.ToReminderPreference(userId), cancellationToken);
        TempData["StatusMessage"] = "Your preferences have been saved.";
        return RedirectToAction(nameof(Accessibility));
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated account has no user identifier.");
}
