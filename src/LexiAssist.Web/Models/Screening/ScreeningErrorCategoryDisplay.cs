using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace LexiAssist.Web.Models.Screening;

public static class ScreeningErrorCategoryDisplay
{
    public static string GetName(ScreeningErrorCategory category)
    {
        var member = typeof(ScreeningErrorCategory).GetMember(category.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? category.ToString();
    }
}
