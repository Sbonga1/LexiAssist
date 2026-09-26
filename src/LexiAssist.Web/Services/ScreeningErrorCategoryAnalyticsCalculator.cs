using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services;

public sealed class ScreeningErrorCategoryAnalyticsCalculator
{
    public IReadOnlyList<ScreeningErrorCategoryAnalyticsItem> Calculate(
        IEnumerable<ScreeningResponseErrorCategory> taggedResponseCategories)
    {
        ArgumentNullException.ThrowIfNull(taggedResponseCategories);

        var tags = taggedResponseCategories.ToArray();
        var classifiedResponseCount = tags
            .Select(tag => tag.ScreeningResponseId)
            .Distinct()
            .Count();
        var responseCounts = tags
            .GroupBy(tag => tag.Category)
            .ToDictionary(
                group => group.Key,
                group => group.Select(tag => tag.ScreeningResponseId).Distinct().Count());

        return Enum.GetValues<ScreeningErrorCategory>()
            .Select(category =>
            {
                var count = responseCounts.GetValueOrDefault(category);
                decimal? rate = classifiedResponseCount == 0
                    ? null
                    : count * 100m / classifiedResponseCount;
                return new ScreeningErrorCategoryAnalyticsItem(
                    category,
                    ScreeningErrorCategoryDisplay.GetName(category),
                    count,
                    classifiedResponseCount,
                    rate);
            })
            .ToArray();
    }
}

public sealed record ScreeningErrorCategoryAnalyticsItem(
    ScreeningErrorCategory Category,
    string CategoryName,
    int ResponseCount,
    int ClassifiedResponseCount,
    decimal? RatePercent);
