using LexiAssist.Web.Services;

namespace LexiAssist.Tests;

public sealed class SouthAfricaTimeFormatterTests
{
    [Fact]
    public void Formats_utc_value_as_cat_without_iso_suffix()
    {
        var value = SouthAfricaTimeFormatter.Format(new DateTime(2026, 9, 22, 16, 7, 9, DateTimeKind.Utc));

        Assert.Equal("2026-09-22 18:07:09 CAT", value);
        Assert.DoesNotContain("T16", value);
        Assert.DoesNotContain("Z", value);
    }

    [Fact]
    public void Treats_unspecified_stored_value_as_utc()
    {
        var value = SouthAfricaTimeFormatter.Format(new DateTime(2026, 9, 22, 16, 7, 9));

        Assert.Equal("2026-09-22 18:07:09 CAT", value);
    }

    [Fact]
    public void Nullable_formatter_returns_null_for_missing_value()
    {
        Assert.Null(SouthAfricaTimeFormatter.Format(null));
    }
}
