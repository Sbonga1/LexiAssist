using LexiAssist.Web.Services;

namespace LexiAssist.Web.ViewModels.Referrals;

public sealed class PsychologistErrorAnalyticsViewModel
{
    public int ClassifiedResponseCount { get; init; }

    public IReadOnlyList<ScreeningErrorCategoryAnalyticsItem> Categories { get; init; } = [];
}
