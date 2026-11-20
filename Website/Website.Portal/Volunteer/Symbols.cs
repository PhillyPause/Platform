namespace Website.Portal.Volunteer;

/// <summary>
/// The Material Symbols (Apache 2.0) the team pages draw, and the one
/// stylesheet that fetches only those glyphs. The font draws a glyph from its
/// name, and a name it was not asked for prints as the word itself, so every
/// icon on the team pages is one of these constants and the URL is built from
/// them.
/// </summary>
public static class Symbols
{
    public const string AccountBalance = "account_balance";
    public const string Apartment = "apartment";
    public const string ArrowDownward = "arrow_downward";
    public const string ArrowUpward = "arrow_upward";
    public const string Badge = "badge";
    public const string CheckCircle = "check_circle";
    public const string Delete = "delete";
    public const string Description = "description";
    public const string DoNotDisturb = "do_not_disturb_on";
    public const string Edit = "edit";
    public const string Event = "event";
    public const string Group = "group";
    public const string Home = "home";
    public const string Inbox = "inbox";
    public const string Person = "person";
    public const string PersonAdd = "person_add";
    public const string Public = "public";
    public const string Schedule = "schedule";
    public const string Send = "send";
    public const string ShieldPerson = "shield_person";
    public const string Unsubscribe = "unsubscribe";
    public const string ViewKanban = "view_kanban";

    public static IReadOnlyList<string> All { get; } =
    [
        AccountBalance, Apartment, ArrowDownward, ArrowUpward, Badge, CheckCircle, Delete, Description,
        DoNotDisturb, Edit, Event, Group, Home, Inbox, Person, PersonAdd, Public, Schedule, Send,
        ShieldPerson, Unsubscribe, ViewKanban
    ];

    /// <summary>
    /// Google Fonts, like the site's text faces: filled, one weight, one size.
    /// The names must be in alphabetical order or Google refuses the subset;
    /// <c>display=block</c> keeps a name from showing as text while it loads.
    /// </summary>
    public static string StylesheetUrl { get; } =
        "https://fonts.googleapis.com/css2?family=Material+Symbols+Rounded:opsz,wght,FILL,GRAD@24,400,1,0&icon_names="
        + string.Join(',', All.Order(StringComparer.Ordinal))
        + "&display=block";
}
