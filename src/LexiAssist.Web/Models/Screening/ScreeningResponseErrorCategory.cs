namespace LexiAssist.Web.Models.Screening;

public sealed class ScreeningResponseErrorCategory
{
    public int ScreeningResponseId { get; set; }

    public ScreeningErrorCategory Category { get; set; }

    public ScreeningResponse ScreeningResponse { get; set; } = null!;
}
