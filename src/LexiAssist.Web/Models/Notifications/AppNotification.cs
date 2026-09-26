using System.ComponentModel.DataAnnotations;
using LexiAssist.Web.Models.Identity;

namespace LexiAssist.Web.Models.Notifications;

public sealed class AppNotification
{
    public int Id { get; set; }

    [Required]
    public string RecipientUserId { get; set; } = string.Empty;

    public AppNotificationType Type { get; set; }

    [Required, MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }

    [MaxLength(2048)]
    public string? RelatedUrl { get; set; }

    public ApplicationUser Recipient { get; set; } = null!;
}
