namespace ManyHands.Core;

/// <summary>
/// The code on a link we hand out, such as <c>phillypause.org/?src=newsguild</c>:
/// it says which partner, poster or post brought someone to join. Lower-case
/// letters, digits and dashes, at most 40, so it is safe in a URL, a CSV and a
/// database column alike. Anything else is ignored rather than refused: a
/// mangled link still lets the person join.
/// </summary>
public static class TrackingCode
{
    public const string QueryName = "src";
    public const int MaxLength = 40;

    /// <summary>The code, lower-cased, or null when there is none or it is not one.</summary>
    public static string? Normalize(string? value)
    {
        var code = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > MaxLength) return null;
        foreach (var c in code)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return null;
        }
        return code;
    }

    /// <summary><paramref name="path"/> with the code on it, or as it was when there is none.</summary>
    public static string Append(string path, string? code) =>
        code is null ? path : $"{path}{(path.Contains('?') ? '&' : '?')}{QueryName}={code}";
}
