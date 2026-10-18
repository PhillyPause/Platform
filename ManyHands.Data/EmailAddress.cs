namespace ManyHands.Data;

/// <summary>Shared by every form that takes an address from a stranger.</summary>
public static class EmailAddress
{
    /// <summary>
    /// Deliberately permissive: real addresses are validated by whether the
    /// confirmation email arrives, not by a regex arms race.
    /// </summary>
    public static bool LooksValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1) return false;
        var domain = value[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.')
               && !value.Contains(' ');
    }
}
