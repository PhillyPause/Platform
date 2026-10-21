namespace Website.Tests;

/// <summary>
/// Locates the repo and its content. The test binary sits several levels deep
/// under bin/, and that depth changes with configuration and framework, so
/// both paths are found rather than counted.
/// </summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    /// Markdown copied beside the test assembly, falling back to the source
    /// tree. The copy is what the host reads at runtime; the fallback keeps the
    /// suite working if the content ever stops being copied.
    /// </summary>
    public static string ContentRoot { get; } = FirstExisting(
        Path.Combine(AppContext.BaseDirectory, "Content"),
        Path.Combine(RepoRoot, "Website", "Website.Content", "Content"));

    private static string FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(Directory.Exists)
        ?? throw new DirectoryNotFoundException(
            $"No content directory among: {string.Join(", ", candidates)}");

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (dir.EnumerateFiles("Platform.slnx").Any())
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"No Platform.slnx above {AppContext.BaseDirectory}; the repo layout moved.");
    }
}
