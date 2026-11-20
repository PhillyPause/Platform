using System.ComponentModel.DataAnnotations;

namespace Website.Portal.Act;

/// <summary>What a reader tells the write and call pages about themselves. Never stored.</summary>
public sealed class DraftInput
{
    public const int StoryLimit = 1200;

    [Required(ErrorMessage = "Your name goes at the bottom, so the office knows who wrote.")]
    [StringLength(80, ErrorMessage = "Keep it to your name.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Where you live is what makes you a constituent.")]
    [StringLength(80, ErrorMessage = "A neighbourhood or town and a zip code is plenty.")]
    public string Place { get; set; } = string.Empty;

    [StringLength(StoryLimit, ErrorMessage = "A sentence or two is best; this is more than a page.")]
    public string? Story { get; set; }

    public DraftTone Tone { get; set; } = DraftTone.Personal;
}
