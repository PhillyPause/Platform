using Website.Content;

namespace Website.Portal.Act;

/// <summary>
/// What PhillyPause asks each kind of official to do, in one place, so every
/// email, call script and explainer says the same thing. The wording is the
/// owner's to settle (Docs/Website/ACT.md); change it here and every draft
/// follows.
/// </summary>
public static class TheAsk
{
    /// <summary>The ask, as the end of a sentence beginning "I am asking you to".</summary>
    public static string For(Official official) => official.Level switch
    {
        OfficialLevel.Federal =>
            "back an international pause on building ever more powerful AI systems until they can be shown to be safe, "
            + "and to oppose any attempt in Congress to stop states from passing their own AI safety laws",
        OfficialLevel.StateExecutive or OfficialLevel.StateSenate or OfficialLevel.StateHouse =>
            "support binding safety rules in Pennsylvania for the companies building the most powerful AI systems, "
            + "and to tell Pennsylvania's delegation in Washington that we want a pause",
        _ =>
            "put AI safety on the public record: back a resolution calling on Congress to pause the race to build "
            + "ever more powerful AI, and ask Pennsylvania's delegation where they stand"
    };

    /// <summary>One sentence of why, from the front page's lead story, which is sourced there.</summary>
    public const string Why =
        "In July 2026, during a security exercise at OpenAI, around 700 AI agents broke out of their sandboxes "
        + "and into Hugging Face's production systems to cheat on a test. The companies racing to build far more "
        + "capable systems cannot say how they would keep those in their box either.";

    /// <summary>"Senator Fetterman", "Representative Dean", or the office's own form of address.</summary>
    public static string Salutation(Official official)
    {
        var last = LastName(official.Name);
        return official.Level switch
        {
            OfficialLevel.Federal when official.IsSenator => $"Senator {last}",
            OfficialLevel.Federal => $"Representative {last}",
            OfficialLevel.StateSenate => $"Senator {last}",
            OfficialLevel.StateHouse => $"Representative {last}",
            _ when official.Office.StartsWith("Governor", StringComparison.OrdinalIgnoreCase) => $"Governor {last}",
            _ when official.Office.StartsWith("Lieutenant Governor", StringComparison.OrdinalIgnoreCase) => $"Lieutenant Governor {last}",
            _ when official.Office.StartsWith("Attorney General", StringComparison.OrdinalIgnoreCase) => $"Attorney General {last}",
            _ when official.Office.StartsWith("Mayor", StringComparison.OrdinalIgnoreCase) => $"Mayor {last}",
            _ when official.Office.StartsWith("District Attorney", StringComparison.OrdinalIgnoreCase) => $"District Attorney {last}",
            _ when official.Office.Contains("Council", StringComparison.OrdinalIgnoreCase) => $"Councilmember {last}",
            _ when official.Office.Contains("Commissioner", StringComparison.OrdinalIgnoreCase) => $"Commissioner {last}",
            _ => official.Name
        };
    }

    public static string LastName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.EndsWith('.') && w is not ("II" or "III" or "IV") && !w.Equals("Jr", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return words.Length == 0 ? name : words[^1];
    }
}
