using System.Text.Json;
using Website.Content;

namespace Website.Portal.Act;

/// <summary>
/// One message to several officials, each sent their own copy. The draft
/// carries tokens; each copy has them filled for its official, because the
/// form of address and the ask both change with the office (<see cref="TheAsk"/>).
/// act.js fills them the same way at the moment the reader sends, so their
/// edits go too: change the two together.
/// </summary>
public static class MailMerge
{
    /// <summary>The most a merge takes: a place's officials, never a list of dozens.</summary>
    public const int Most = 12;

    public const string Salutation = "{salutation}";
    public const string Ask = "{ask}";
    public const string Name = "{name}";

    /// <summary>The text with every token filled for <paramref name="official"/>; a token's case does not matter.</summary>
    public static string Fill(string text, Official official) => text
        .Replace(Salutation, TheAsk.Salutation(official), StringComparison.OrdinalIgnoreCase)
        .Replace(Ask, TheAsk.For(official), StringComparison.OrdinalIgnoreCase)
        .Replace(Name, official.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>What act.js fills in, keyed by the token's word: the data-fill attribute's JSON.</summary>
    public static string Values(Official official) => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["salutation"] = TheAsk.Salutation(official),
        ["ask"] = TheAsk.For(official),
        ["name"] = official.Name
    });

    /// <summary>True when a draft still carries what makes it a merge: a greeting and an ask for each to fill.</summary>
    public static bool Carries(string body) =>
        body.Contains(Salutation, StringComparison.OrdinalIgnoreCase) && body.Contains(Ask, StringComparison.OrdinalIgnoreCase);
}
