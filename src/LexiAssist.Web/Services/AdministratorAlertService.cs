using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LexiAssist.Web.Services;

public sealed class AdministratorAlertService(
    ApplicationDbContext dbContext,
    IOptions<AdministratorAlertOptions> configuredOptions,
    TimeProvider timeProvider) : IAdministratorAlertService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AdministratorAlertOptions options = configuredOptions.Value;

    public async Task<IReadOnlyList<AdministratorAlert>> GetForAdministratorAsync(
        string administratorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(administratorId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var alerts = new List<AdministratorAlert>();
        var maximumAlerts = Math.Max(1, options.MaximumAlerts);

        var notifications = await dbContext.AppNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == administratorId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Take(maximumAlerts)
            .Select(notification => new AdministratorAlert(
                notification.Id,
                AdministratorAlertKind.Notification,
                notification.Title,
                notification.Message,
                notification.CreatedAtUtc,
                notification.ReadAtUtc,
                notification.RelatedUrl,
                false))
            .ToListAsync(cancellationToken);
        alerts.AddRange(notifications);

        await AddScreeningAttentionAlertAsync(alerts, now, cancellationToken);
        await AddReferralAgeingAlertAsync(alerts, now, cancellationToken);
        await AddTechnicalFailureAlertAsync(alerts, now, cancellationToken);
        await AddContentConfigurationAlertAsync(alerts, now, cancellationToken);

        return alerts
            .OrderByDescending(alert => alert.CreatedAtUtc)
            .ThenByDescending(alert => alert.Id ?? 0)
            .Take(maximumAlerts)
            .ToArray();
    }

    private async Task AddScreeningAttentionAlertAsync(
        ICollection<AdministratorAlert> alerts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-Math.Max(1, options.IncompleteScreeningAgeHours));
        var attempts = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .Where(attempt => attempt.Status == ScreeningAttemptStatus.Abandoned ||
                              (attempt.Status == ScreeningAttemptStatus.InProgress && attempt.StartedAtUtc <= cutoff))
            .Select(attempt => new { attempt.Status, attempt.StartedAtUtc })
            .ToListAsync(cancellationToken);
        if (attempts.Count == 0)
        {
            return;
        }

        var abandonedCount = attempts.Count(attempt => attempt.Status == ScreeningAttemptStatus.Abandoned);
        var incompleteCount = attempts.Count - abandonedCount;
        var parts = new List<string>();
        if (incompleteCount > 0)
        {
            parts.Add(incompleteCount + " incomplete screening " + (incompleteCount == 1 ? "attempt" : "attempts"));
        }

        if (abandonedCount > 0)
        {
            parts.Add(abandonedCount + " abandoned screening " + (abandonedCount == 1 ? "attempt" : "attempts"));
        }

        alerts.Add(new AdministratorAlert(
            null,
            AdministratorAlertKind.ScreeningAttention,
            "Screening attempts need attention",
            string.Join(" and ", parts) + " may need review. Check the screening workflow before contacting a learner.",
            attempts.Min(attempt => attempt.StartedAtUtc),
            null,
            "/admin/screening-reports",
            true));
    }

    private async Task AddReferralAgeingAlertAsync(
        ICollection<AdministratorAlert> alerts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var ageHours = Math.Max(1, options.PendingReferralAgeHours);
        var cutoff = now.AddHours(-ageHours);
        var referrals = await dbContext.Referrals
            .AsNoTracking()
            .Where(referral => referral.LearnerId == referral.ScreeningReport.UserId &&
                               referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
                               referral.ScreeningReport.IsReferralRecommended &&
                               referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                               referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null &&
                               referral.Status == ReferralStatus.Pending &&
                               referral.CreatedAtUtc <= cutoff)
            .Select(referral => referral.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        if (referrals.Count == 0)
        {
            return;
        }

        alerts.Add(new AdministratorAlert(
            null,
            AdministratorAlertKind.ReferralAgeing,
            "Pending referrals are ageing",
            referrals.Count + " pending " + (referrals.Count == 1 ? "referral has" : "referrals have") +
            " been waiting longer than the configured " + ageHours + "-hour review period.",
            referrals.Min(),
            null,
            "/admin/referrals?status=Pending",
            true));
    }

    private async Task AddTechnicalFailureAlertAsync(
        ICollection<AdministratorAlert> alerts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var windowHours = Math.Max(1, options.TechnicalFailureWindowHours);
        var cutoff = now.AddHours(-windowHours);
        var activityMetadata = await dbContext.ActivityAttempts
            .AsNoTracking()
            .Where(attempt => attempt.StartedAtUtc >= cutoff &&
                              attempt.MetadataJson != null &&
                              attempt.MetadataJson != string.Empty)
            .Select(attempt => new { attempt.StartedAtUtc, attempt.MetadataJson })
            .ToListAsync(cancellationToken);
        var responseDetails = await dbContext.ScreeningResponses
            .AsNoTracking()
            .Where(response => response.CreatedAtUtc >= cutoff &&
                               response.ResponseDetailsJson != null &&
                               response.ResponseDetailsJson != string.Empty)
            .Select(response => new { response.CreatedAtUtc, response.ResponseDetailsJson })
            .ToListAsync(cancellationToken);

        var invalidRecords = activityMetadata
            .Where(item => !IsValidJson(item.MetadataJson))
            .Select(item => item.StartedAtUtc)
            .Concat(responseDetails
                .Where(item => !IsValidJson(item.ResponseDetailsJson))
                .Select(item => item.CreatedAtUtc))
            .ToArray();
        var threshold = Math.Max(1, options.RepeatedTechnicalFailureThreshold);
        if (invalidRecords.Length < threshold)
        {
            return;
        }

        alerts.Add(new AdministratorAlert(
            null,
            AdministratorAlertKind.TechnicalFailure,
            "Repeated technical failures detected",
            "LexiAssist detected " + invalidRecords.Length +
            " saved records with unreadable technical state in the last " + windowHours +
            " hours. Review activity or screening configuration and application logs.",
            invalidRecords.Max(),
            null,
            "/admin/screening-tests",
            true));
    }

    private async Task AddContentConfigurationAlertAsync(
        ICollection<AdministratorAlert> alerts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var activeTests = await dbContext.ScreeningTests
            .AsNoTracking()
            .Where(test => test.IsActive)
            .Select(test => new
            {
                test.Name,
                ActiveQuestionCount = test.Questions.Count(question => question.IsActive)
            })
            .ToListAsync(cancellationToken);
        var activeQuestions = await dbContext.ScreeningQuestions
            .AsNoTracking()
            .Where(question => question.IsActive && question.ScreeningTest.IsActive)
            .Select(question => new QuestionConfigRow(
                question.Prompt,
                question.QuestionType,
                question.MetadataJson,
                question.AudioPath))
            .ToListAsync(cancellationToken);
        var activeActivities = await dbContext.LearningActivities
            .AsNoTracking()
            .Where(activity => activity.IsActive)
            .Select(activity => new { activity.Name, activity.Description, activity.Instructions })
            .ToListAsync(cancellationToken);

        var testsWithoutQuestions = activeTests.Count(test => test.ActiveQuestionCount == 0);
        var invalidQuestions = activeQuestions.Count(HasQuestionConfigurationIssue);
        var invalidActivities = activeActivities.Count(activity =>
            string.IsNullOrWhiteSpace(activity.Name) ||
            string.IsNullOrWhiteSpace(activity.Description) ||
            string.IsNullOrWhiteSpace(activity.Instructions));
        var issueCount = testsWithoutQuestions + invalidQuestions + invalidActivities;
        if (issueCount == 0)
        {
            return;
        }

        alerts.Add(new AdministratorAlert(
            null,
            AdministratorAlertKind.ContentConfiguration,
            "Content configuration needs review",
            "LexiAssist found " + issueCount + " active content configuration " +
            (issueCount == 1 ? "issue" : "issues") +
            ". Review screening questions and learning activity instructions before use.",
            now,
            null,
            "/admin/screening-tests",
            true));
    }

    private static bool HasQuestionConfigurationIssue(QuestionConfigRow question)
    {
        if (string.IsNullOrWhiteSpace(question.Prompt))
        {
            return true;
        }

        if (question.QuestionType == ScreeningQuestionType.ListeningResponse &&
            string.IsNullOrWhiteSpace(question.AudioPath))
        {
            return true;
        }

        if (question.MetadataJson is not string metadataJson || string.IsNullOrWhiteSpace(metadataJson))
        {
            return question.QuestionType is ScreeningQuestionType.MultipleChoice or
                ScreeningQuestionType.PhonologicalChoice or
                ScreeningQuestionType.ReadingPassage or
                ScreeningQuestionType.ListeningResponse or
                ScreeningQuestionType.TimedNaming;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ScreeningQuestionMetadata>(metadataJson, JsonOptions);
            return question.QuestionType switch
            {
                ScreeningQuestionType.MultipleChoice or ScreeningQuestionType.PhonologicalChoice =>
                    metadata?.ChoiceOptions is not { Count: >= 2 } ||
                    metadata.ChoiceOptions.Any(string.IsNullOrWhiteSpace),
                ScreeningQuestionType.ReadingPassage => string.IsNullOrWhiteSpace(metadata?.PassageText),
                ScreeningQuestionType.TimedNaming => metadata?.TimedNamingItems is not { Count: 20 },
                _ => false
            };
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool IsValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record QuestionConfigRow(
        string Prompt,
        ScreeningQuestionType QuestionType,
        string? MetadataJson,
        string? AudioPath);
}
