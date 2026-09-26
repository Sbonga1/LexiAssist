using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.ViewModels.Referrals;

public sealed class UpdateResponseErrorCategoriesViewModel
{
    public List<ScreeningErrorCategory>? ErrorCategories { get; init; } = [];
}
