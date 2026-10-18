using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// Static server rendering is the default, and every interactive opt-in costs
/// the reader a WebAssembly download (Docs/PLAN.md section 2.1). That argument
/// is the reason the site is a Blazor Web App rather than standalone WASM, and
/// it erodes one <c>@rendermode</c> at a time if nothing is watching.
///
/// So interactive components are listed here on purpose. Adding one to the
/// allowlist is the moment to justify it; forgetting to is a failing test
/// rather than a quietly heavier page.
/// </summary>
public class RenderModeBudgetTests
{
    /// <summary>Razor files allowed to declare an interactive render mode.</summary>
    private static readonly string[] AllowedInteractiveComponents = [];

    [Fact]
    public void Only_allowlisted_components_declare_an_interactive_render_mode()
    {
        var interactive = RazorFiles()
            .Where(path => File.ReadAllText(path).Contains("@rendermode", StringComparison.OrdinalIgnoreCase))
            .Select(RepoRelative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        interactive.Should().BeEquivalentTo(
            AllowedInteractiveComponents,
            "every interactive component costs the reader a WASM download — add it to " +
            $"{nameof(AllowedInteractiveComponents)} deliberately, or render it statically");
    }

    [Fact]
    public void The_component_tree_is_discoverable()
    {
        // Guards the test above against silently passing because the path walk
        // broke (a rename, a restructure, a different working directory).
        RazorFiles().Should().NotBeEmpty("the render-mode scan has nothing to scan");
    }

    private static IEnumerable<string> RazorFiles() =>
        new[] { "Website.Portal", "Website.Client" }
            .Select(project => Path.Combine(TestPaths.RepoRoot, project))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string RepoRelative(string path) =>
        Path.GetRelativePath(TestPaths.RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
