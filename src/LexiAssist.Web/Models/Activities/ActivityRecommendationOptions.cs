namespace LexiAssist.Web.Models.Activities;

public sealed class ActivityRecommendationOptions
{
    /// <summary>
    /// Keys are names from saved screening flags or response error categories.
    /// Values are names of activities in the active learning activity catalogue.
    /// </summary>
    public Dictionary<string, List<string>> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
