using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// A wheel or swipe over an open dialog scrolls the dialog or nothing, never
/// the page behind it. The dialogs are pure CSS, so these pin the two rules
/// that keep that true.
/// </summary>
public class DialogScrollTests
{
    private static readonly string Styles = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "wwwroot", "broadsheet.css"));

    [Fact]
    public void The_page_holds_still_while_a_dialog_is_open() =>
        Styles.Should().Contain("html:has(.dlgtoggle:checked:not(#dlg-none)) { overflow: hidden; }");

    [Fact]
    public void A_long_dialog_keeps_its_scroll_to_itself() =>
        Styles.Should().MatchRegex(@"\.paneldlg-body \{[^}]*overscroll-behavior: contain;");
}
