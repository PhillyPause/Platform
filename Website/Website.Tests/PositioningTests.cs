using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// PhillyPause is an independent Philadelphia organisation, loosely affiliated
/// with PauseAI Global. It is <b>not</b> a local group, chapter or branch of
/// PauseAI US — PauseAI US dropped all of its local groups, PhillyPause among
/// them (Docs/PLAN.md section 0.0).
///
/// This has already been got wrong once, across every page at once, which is
/// exactly the kind of error prose review misses and a test does not. The
/// claim is in body copy, meta descriptions, press boilerplate and the footer,
/// so the check is over everything that reaches a reader.
/// </summary>
public class PositioningTests
{
    /// <summary>
    /// Phrases that assert a subordinate relationship. Matched case-insensitively
    /// against all reader-facing text.
    /// </summary>
    private static readonly string[] ForbiddenClaims =
    [
        "local group of",
        "local group under",
        "a local group",
        "chapter of pauseai",
        "pauseai us chapter",
        "part of pauseai",
        "under pauseai",
        "branch of pauseai",
        "affiliate of pauseai us",
        "we are a chapter"
    ];

    /// <summary>
    /// Denying the relationship necessarily quotes it, so these exact phrasings
    /// are removed before scanning. Listed in full rather than matched loosely
    /// for "not": a fuzzy negation check would let a real claim through.
    /// </summary>
    private static readonly string[] AllowedDenials =
    [
        "not a local group of PauseAI US",
        "local group of any national organisation",
        "a local group of any national organisation"
    ];

    [Theory]
    [MemberData(nameof(ReaderFacingFiles))]
    public void No_file_claims_PhillyPause_is_someone_elses_local_group(string relativePath)
    {
        // The Markdown is hard-wrapped, so a phrase can straddle a newline.
        // Collapse whitespace first or every multi-word check is a coin flip.
        var text = CollapseWhitespace(File.ReadAllText(Path.Combine(TestPaths.RepoRoot, relativePath)));

        foreach (var denial in AllowedDenials)
            text = text.Replace(denial, string.Empty, StringComparison.OrdinalIgnoreCase);

        var found = ForbiddenClaims
            .Where(claim => text.Contains(claim, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        found.Should().BeEmpty(
            $"{relativePath} claims a subordinate relationship PhillyPause does not have. " +
            "It is independent and loosely affiliated with PauseAI Global — see PLAN.md 0.0");
    }

    private static string CollapseWhitespace(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    /// <summary>
    /// Every Markdown page and every Razor component whose text a reader sees.
    /// Enumerated rather than hard-coded so a new page is covered by default —
    /// the failure mode is a page added later that reintroduces the claim.
    /// </summary>
    public static TheoryData<string> ReaderFacingFiles()
    {
        var data = new TheoryData<string>();

        var roots = new[]
        {
            Path.Combine(TestPaths.RepoRoot, "Website", "Website.Content", "Content"),
            Path.Combine(TestPaths.RepoRoot, "Website", "Website.Portal", "Components")
        };

        var files = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory
                .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => Path.GetRelativePath(TestPaths.RepoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var file in files)
            data.Add(file);

        return data;
    }
}
