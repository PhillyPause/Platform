using System.Text.RegularExpressions;
using FluentAssertions;

namespace Website.Tests;

/// <summary>
/// The explainer's script lives in three places that must agree: the scene
/// markup, the caption timeline in JavaScript, and the transcript. Drift
/// between them is invisible in review and obvious to a reader — a caption
/// describing a scene that is no longer on screen.
/// </summary>
public class ExplainerTests
{
    private const int SceneCount = 8;

    private static readonly string Markup = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "Components", "Shared", "Explainer.razor"));

    private static readonly string Timeline = File.ReadAllText(Path.Combine(
        TestPaths.RepoRoot, "Website.Portal", "wwwroot", "explainer.js"));

    [Fact]
    public void Markup_has_one_scene_per_script_beat() =>
        Regex.Matches(Markup, @"class=""ex-scene[^""]*"" data-index=""\d+""")
            .Count.Should().Be(SceneCount);

    [Fact]
    public void Scene_indexes_run_one_to_eight()
    {
        var indexes = Regex.Matches(Markup, @"data-index=""(\d+)""")
            .Select(match => int.Parse(match.Groups[1].Value))
            .OrderBy(index => index);

        indexes.Should().Equal(Enumerable.Range(1, SceneCount));
    }

    [Fact]
    public void Timeline_has_one_caption_per_scene() =>
        Regex.Matches(Timeline, @"\{\s*at:\s*\d+,\s*text:").Count.Should().Be(SceneCount);

    [Fact]
    public void Timeline_marks_are_in_order_and_inside_the_duration()
    {
        var marks = Regex.Matches(Timeline, @"at:\s*(\d+)")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToArray();

        marks.Should().BeInAscendingOrder();
        marks[0].Should().Be(0, "the first scene starts at zero");

        var duration = int.Parse(Regex.Match(Timeline, @"DURATION\s*=\s*(\d+)").Groups[1].Value);
        marks.Should().OnlyContain(mark => mark < duration);
    }

    [Fact]
    public void Transcript_has_one_entry_per_scene()
    {
        var transcript = Markup[Markup.IndexOf("ex-transcript-body", StringComparison.Ordinal)..];

        Regex.Matches(transcript, "<li>").Count.Should().Be(
            SceneCount,
            "the transcript is the content, and the fallback with no JavaScript or audio");
    }

    /// <summary>
    /// The explainer is static markup plus a JavaScript module precisely so the
    /// page ships no WebAssembly. An interactive render mode here would undo
    /// that; RenderModeBudgetTests catches it globally, this names the reason.
    /// </summary>
    [Fact]
    public void Explainer_is_not_an_interactive_component() =>
        Markup.Should().NotContain("@rendermode");

    /// <summary>
    /// Narration is an enhancement. If the audio file is absent the piece has
    /// to keep working on a timer, so nothing may assume the element loaded.
    /// </summary>
    [Fact]
    public void Narration_is_optional()
    {
        Markup.Should().Contain("preload=\"none\"", "nothing downloads before Play");
        Timeline.Should().Contain("useAudio = false", "there is a path back to the timer");
        Timeline.Should().Contain("catch", "a rejected play() must not strand the reader");
    }
}
