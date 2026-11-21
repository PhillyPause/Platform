using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Website.Content;
using Website.Portal.Act;

namespace Website.Tests;

/// <summary>
/// One message to several officials, a copy each: the draft greets and asks
/// with tokens, each copy is filled for its own office, the reader sends every
/// one themselves, and the page is reached from Your officials.
/// </summary>
public partial class ActMergeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly OfficialsStore Store = new(TestPaths.ContentRoot);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // A senator, who takes a form, and a county commissioner, who takes email:
    // two forms of address, two asks, both ways of sending.
    private static Official Senator => Store.All.First(o => o.IsSenator && o.CanEmail);

    private static Official Commissioner => Store.All.First(o => o.Level == OfficialLevel.County && o.Email is not null);

    private static string Both => ActPaths.WriteTo([Senator, Commissioner]);

    [Fact]
    public void Each_copy_is_filled_for_its_own_office()
    {
        const string text = "Dear {Salutation},\nI am asking you to {ask}. Thank you, {NAME}.";

        var forRep = MailMerge.Fill(text, Commissioner);

        forRep.Should().StartWith($"Dear {TheAsk.Salutation(Commissioner)},")
            .And.Contain(TheAsk.For(Commissioner)).And.Contain(Commissioner.Name).And.NotContain("{");
        MailMerge.Fill(text, Senator).Should().Contain(TheAsk.For(Senator)).And.NotContain(TheAsk.For(Commissioner));

        using var values = JsonDocument.Parse(MailMerge.Values(Commissioner));
        values.RootElement.GetProperty("salutation").GetString().Should().Be(TheAsk.Salutation(Commissioner), "act.js fills from these");
    }

    [Fact]
    public void A_template_merge_greets_and_asks_with_tokens_and_names_nobody()
    {
        var draft = TemplateDrafter.Write(Merge("I have two kids"));

        draft.Body.Should().StartWith("Dear {salutation},").And.Contain("I am asking you to {ask}.")
            .And.Contain("I have two kids.").And.NotContain(TheAsk.Salutation(Senator)).And.NotContain(TheAsk.For(Senator));
        MailMerge.Carries(draft.Body).Should().BeTrue();
    }

    [Theory]
    [InlineData("Subject: Act on AI\n\nDear {salutation},\nI am asking you to {ask}.\n\nSam", true)]
    [InlineData("Subject: Act on AI\n\nDear Senator,\nI am asking you to back a pause.\n\nSam", false)]
    public async Task A_model_merge_is_used_only_if_it_kept_the_tokens(string reply, bool used)
    {
        var handler = new StubHandler(JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = reply } } } }));
        var options = Options.Create(new DeepInfraOptions { ApiKey = "k", Model = "m" });
        var drafter = new DeepInfraDrafter(new HttpClient(handler), options, new ModelBudget(options), NullLogger<DeepInfraDrafter>.Instance);

        var draft = await drafter.DraftAsync(Merge(null));

        draft.AiAssisted.Should().Be(used);
        MailMerge.Carries(draft.Body).Should().BeTrue("either way, each copy can be filled");
        using var sent = JsonDocument.Parse(handler.Body);
        sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should()
            .Contain(Senator.Name).And.Contain(Commissioner.Name).And.Contain("Dear {salutation},").And.Contain("I am asking you to {ask}.");
    }

    [Fact]
    public async Task The_page_names_its_three_steps_and_who_it_goes_to()
    {
        var page = await factory.CreateClient().GetStringAsync(Both);

        page.Should().Contain("Write to all 2 at once")
            .And.Contain($"href=\"{ActPaths.WriteTo([Commissioner])}\" aria-current=\"true\"", "dropping the senator leaves the commissioner")
            .And.Contain("<h3 id=\"you-head\">About you</h3>")
            .And.Contain("<h3>Your message</h3>").And.Contain("<h3>Send each one</h3>");
    }

    [Fact]
    public async Task A_draft_gives_each_official_their_own_filled_copy_and_keeps_the_tokens_in_the_editor()
    {
        var response = await SubmitAsync(factory.CreateClient(), Both, "draft",
            ("Input.Name", "Sam Rivera"), ("Input.Place", "Fishtown, 19125"), ("Input.Story", "I teach eighth grade."), ("Input.Tone", "Personal"));

        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        page.Should().Contain("Dear {salutation},", "the reader edits one message")
            .And.Contain($"mailto:{Commissioner.Email}?subject=")
            .And.Contain(Uri.EscapeDataString($"Dear {TheAsk.Salutation(Commissioner)},"), "the commissioner's link is filled for them")
            .And.Contain("data-fill=\"{\"salutation\":")
            .And.Contain($"Email {TheAsk.Salutation(Commissioner)}")
            .And.NotContain("%7Bsalutation%7D", "no link carries an unfilled token")
            .And.NotContain("DeepInfra");
        if (Senator.Email is null)
            page.Should().Contain("Copy theirs").And.Contain($"href=\"{Senator.ContactForm}\" target=\"_blank\" rel=\"noopener\"");
    }

    [Fact]
    public async Task One_official_is_the_ordinary_write_page_and_nobody_is_not_found()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var one = await client.GetAsync(ActPaths.WriteTo([Commissioner]));
        one.StatusCode.Should().Be(HttpStatusCode.Redirect);
        one.Headers.Location!.ToString().Should().EndWith($"/act/write/{Commissioner.Key}");

        (await client.GetAsync("/act/write?to=nobody-at-all")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/act/write")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Your_officials_offers_to_write_to_everyone_in_a_place_at_once()
    {
        var match = new DistrictMatch("3", "1", "182", "Philadelphia");
        var harrisburg = Store.For(match, Today)
            .Where(o => o.Arena == "Harrisburg" && o.CanEmail && o.Status != OfficialStatus.Departing).ToArray();
        harrisburg.Should().HaveCountGreaterThan(1, "the test needs a place with more than one to write to");

        var page = await factory.CreateClient().GetStringAsync(ActPaths.Yours(match, "harrisburg", null));

        page.Should().Contain($"<a class=\"act-btn\" href=\"{ActPaths.WriteTo(harrisburg).Replace("&", "&amp;")}\">Write to all {harrisburg.Length} at once</a>");
    }

    private static DraftRequest Merge(string? story) =>
        new(Senator, DraftKind.Email, "Sam", "19134", story, DraftTone.Personal) { Recipients = [Senator, Commissioner] };

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, string path, string handler, params (string Name, string Value)[] fields)
    {
        var page = await client.GetStringAsync(path);
        var token = Token().Match(page).Groups[1].Value;
        var body = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token), new("_handler", handler) };
        body.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
        return await client.PostAsync(path, new FormUrlEncodedContent(body));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex Token();
}
