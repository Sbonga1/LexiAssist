using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;

namespace LexiAssist.Tests;

public sealed class ScreeningErrorCategoryAnalyticsCalculatorTests
{
    private readonly ScreeningErrorCategoryAnalyticsCalculator _calculator = new();

    [Fact]
    public void Calculates_category_rates_against_distinct_responses_with_any_tag()
    {
        var tags = new[]
        {
            Tag(1, ScreeningErrorCategory.ShortVowel),
            Tag(1, ScreeningErrorCategory.Omission),
            Tag(1, ScreeningErrorCategory.Omission),
            Tag(2, ScreeningErrorCategory.ShortVowel),
            Tag(3, ScreeningErrorCategory.Decoding),
            Tag(4, ScreeningErrorCategory.Omission)
        };

        var results = _calculator.Calculate(tags);
        var shortVowels = Assert.Single(results, item => item.Category == ScreeningErrorCategory.ShortVowel);
        var omissions = Assert.Single(results, item => item.Category == ScreeningErrorCategory.Omission);
        var longVowels = Assert.Single(results, item => item.Category == ScreeningErrorCategory.LongVowel);

        Assert.Equal(4, shortVowels.ClassifiedResponseCount);
        Assert.Equal(2, shortVowels.ResponseCount);
        Assert.Equal(50m, shortVowels.RatePercent);
        Assert.Equal(2, omissions.ResponseCount);
        Assert.Equal(50m, omissions.RatePercent);
        Assert.Equal(0, longVowels.ResponseCount);
        Assert.Equal(0m, longVowels.RatePercent);
    }

    [Fact]
    public void Does_not_report_rates_when_no_response_has_a_category()
    {
        var results = _calculator.Calculate([]);

        Assert.Equal(Enum.GetValues<ScreeningErrorCategory>().Length, results.Count);
        Assert.All(results, item =>
        {
            Assert.Equal(0, item.ResponseCount);
            Assert.Equal(0, item.ClassifiedResponseCount);
            Assert.Null(item.RatePercent);
        });
    }

    private static ScreeningResponseErrorCategory Tag(int responseId, ScreeningErrorCategory category) => new()
    {
        ScreeningResponseId = responseId,
        Category = category
    };
}
