namespace LexiAssist.Web.Services;

public static class SouthAfricaTimeFormatter
{
    private static readonly TimeZoneInfo SouthAfricaTimeZone = ResolveTimeZone();

    public static DateTime ToUtcStartOfDay(DateOnly date)
    {
        var localStart = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localStart, SouthAfricaTimeZone);
    }

    public static string Format(DateTime utcValue)
    {
        var utc = utcValue.Kind switch
        {
            DateTimeKind.Utc => utcValue,
            DateTimeKind.Local => utcValue.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcValue, DateTimeKind.Utc)
        };

        var localValue = TimeZoneInfo.ConvertTimeFromUtc(utc, SouthAfricaTimeZone);
        return $"{localValue:yyyy-MM-dd HH:mm:ss} CAT";
    }

    public static string? Format(DateTime? utcValue) =>
        utcValue is { } value ? Format(value) : null;

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var identifier in new[] { "Africa/Johannesburg", "South Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identifier);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new TimeZoneNotFoundException("A South African time zone is not available on this system.");
    }
}
