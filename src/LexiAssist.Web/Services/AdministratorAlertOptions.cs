namespace LexiAssist.Web.Services;

public sealed class AdministratorAlertOptions
{
    public const string SectionName = "AdministratorAlerts";

    public int PendingReferralAgeHours { get; set; } = 48;

    public int IncompleteScreeningAgeHours { get; set; } = 24;

    public int RepeatedTechnicalFailureThreshold { get; set; } = 3;

    public int TechnicalFailureWindowHours { get; set; } = 24;

    public int MaximumAlerts { get; set; } = 20;
}
