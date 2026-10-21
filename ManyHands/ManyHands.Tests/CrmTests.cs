using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ManyHands.Core;
using ManyHands.Data;
using ManyHands.Data.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Website.Portal.Volunteer;

namespace ManyHands.Tests;

/// <summary>
/// The CRM and outreach mail: the timeline that gathers everything said to an
/// office, and threads that hold together whichever way a reply comes back.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CrmTests(DatabaseFixture db)
{
    private const string Secret = "test-only-inbound-secret";

    private CrmRepository Crm => new(db.DataSource);
    private CrmEmailRepository Emails => new(db.DataSource);

    private OutreachEmail Outreach() => new(
        Emails, Crm,
        new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
        Options.Create(new EmailOptions { OutreachAddress = "outreach@example.test" }),
        NullLogger<OutreachEmail>.Instance);

    private Task<Guid> OrganizationAsync(string? externalRef = null) =>
        Crm.CreateOrganizationAsync(
            new OrganizationFields(OrganizationKind.CongressionalOffice, $"Office {Guid.NewGuid():N}", null, null, null, "PA", "03"),
            externalRef, createdBy: null);

    [Fact]
    public async Task Importing_the_same_office_twice_finds_the_same_organization()
    {
        var bioguide = $"T{Guid.NewGuid():N}"[..8];

        var first = await OrganizationAsync(bioguide);
        var second = await OrganizationAsync(bioguide);

        second.Should().Be(first);
    }

    [Fact]
    public async Task A_removed_organization_is_hidden_and_its_history_kept()
    {
        var org = await OrganizationAsync();
        await Crm.LogInteractionAsync(org, null, null, InteractionKind.Call, DateTimeOffset.UtcNow, "Called.", null);

        (await Crm.DeleteOrganizationAsync(org)).Should().BeTrue();

        (await Crm.GetOrganizationAsync(org)).Should().BeNull();
        (await Crm.GetOrganizationsAsync()).Should().NotContain(o => o.Id == org);
        (await Crm.GetTimelineAsync(organizationId: org)).Should().ContainSingle();
    }

    [Fact]
    public async Task What_was_said_to_a_contact_shows_on_their_organization()
    {
        var org = await OrganizationAsync();
        var contact = await Crm.SaveContactAsync(null, org, "Jamie Staffer", "Scheduler", null, null, null);

        await Crm.LogInteractionAsync(null, contact, null, InteractionKind.Meeting, DateTimeOffset.UtcNow, "Met in the district office.", null);

        var entry = (await Crm.GetTimelineAsync(organizationId: org)).Should().ContainSingle().Subject;
        entry.ContactName.Should().Be("Jamie Staffer");
        entry.Kind.Should().Be(InteractionKind.Meeting);
    }

    [Fact]
    public async Task A_task_is_done_once_and_late_only_while_open()
    {
        var org = await OrganizationAsync();
        var id = await Crm.AddTaskAsync("Send the letter", null, org, new DateOnly(2026, 1, 1), null);
        var today = new DateOnly(2026, 2, 1);

        var open = (await Crm.GetTasksAsync(CrmTaskStatus.Open, organizationId: org)).Should().ContainSingle().Subject;
        open.IsOverdueOn(today).Should().BeTrue();

        await Crm.SetTaskStatusAsync(id, CrmTaskStatus.Done);

        (await Crm.GetTasksAsync(CrmTaskStatus.Open, organizationId: org)).Should().BeEmpty();
        (await Crm.GetTasksAsync(organizationId: org)).Single().IsOverdueOn(today).Should().BeFalse();
    }

    [Fact]
    public async Task Sending_records_the_message_and_puts_it_on_the_timeline()
    {
        var org = await OrganizationAsync();
        var contact = await Crm.SaveContactAsync(null, org, "Jamie", null, TestData.NewEmail(), null, null);

        var sent = await Outreach().SendAsync((await Crm.GetContactAsync(contact))!.Email!, "Meeting request", "Hello.",
            org, contact, null, null, null);

        sent.Status.Should().Be(EmailStatus.Simulated);
        sent.MessageId.Should().StartWith("<").And.EndWith("@example.test>");
        (await Emails.GetThreadAsync(sent.ConversationId)).Should().ContainSingle();
        (await Crm.GetTimelineAsync(organizationId: org)).Should().ContainSingle(e => e.Body == "Sent: Meeting request");
    }

    [Fact]
    public async Task A_reply_naming_our_message_joins_its_thread()
    {
        var address = TestData.NewEmail();
        var sent = await Outreach().SendAsync(address, "Hello", "Hi.", null, null, null, null, null);

        var reply = await Outreach().ReceiveAsync(new InboundEmail(
            $"Jamie <{address}>", "outreach@example.test", "Re: Hello", "Thanks!", $"<{Guid.NewGuid():N}@example.test>",
            sent.MessageId, null));

        reply!.ConversationId.Should().Be(sent.ConversationId);
    }

    [Fact]
    public async Task A_reply_that_lost_our_message_id_still_finds_the_thread_by_its_sender()
    {
        var address = TestData.NewEmail();
        var sent = await Outreach().SendAsync(address, "Hello", "Hi.", null, null, null, null, null);

        var reply = await Outreach().ReceiveAsync(new InboundEmail(
            address, "outreach@example.test", "Re: Hello", "Thanks!", null, "<a-provider-rewrote-it@example.test>", null));

        reply!.ConversationId.Should().Be(sent.ConversationId);
    }

    [Fact]
    public async Task Mail_from_a_stranger_is_kept_for_triage_and_filing_clears_it()
    {
        var org = await OrganizationAsync();
        var stranger = TestData.NewEmail();

        var received = await Outreach().ReceiveAsync(new InboundEmail(
            stranger, "outreach@example.test", "Partnership?", "We should talk.", null, null, null));

        received!.NeedsTriage.Should().BeTrue();
        (await Emails.GetThreadsAsync(triageOnly: true)).Should().Contain(t => t.ConversationId == received.ConversationId);

        var contact = await Crm.SaveContactAsync(null, org, "New Friend", null, stranger, null, null);
        await Emails.TriageAsync(received.ConversationId, contact, org);

        (await Emails.GetThreadsAsync(triageOnly: true)).Should().NotContain(t => t.ConversationId == received.ConversationId);
        (await Emails.GetThreadAsync(received.ConversationId)).Single().OrganizationId.Should().Be(org);
    }

    [Fact]
    public async Task The_same_inbound_message_delivered_twice_is_stored_once()
    {
        var messageId = $"<{Guid.NewGuid():N}@example.test>";
        var message = new InboundEmail(TestData.NewEmail(), "outreach@example.test", "Twice", "Same.", messageId, null, null);

        var first = await Outreach().ReceiveAsync(message);
        await Outreach().ReceiveAsync(message);

        (await Emails.GetThreadAsync(first!.ConversationId)).Should().ContainSingle();
    }

    [Fact]
    public void Outgoing_mail_carries_the_threading_headers()
    {
        var headers = ResendEmailSender.BuildHeaders(new OutgoingEmail(
            "to@example.test", "Re: Hello", "Hi.", MessageId: "<ours@example.test>", InReplyTo: "<theirs@example.test>"));

        headers.Should().Contain("Message-ID", "<ours@example.test>")
            .And.Contain("In-Reply-To", "<theirs@example.test>")
            .And.Contain("References", "<theirs@example.test>");
    }

    [Theory]
    [InlineData("Jamie Lee <jamie@example.test>", "jamie@example.test")]
    [InlineData("\"Lee, Jamie\" <jamie@example.test>", "jamie@example.test")]
    [InlineData("  jamie@example.test  ", "jamie@example.test")]
    public void A_display_name_is_stripped_to_the_address(string from, string expected) =>
        OutreachEmail.SenderAddress(from).Should().Be(expected);

    [Fact]
    public async Task The_inbound_webhook_is_off_without_a_secret_and_refuses_a_wrong_one()
    {
        var payload = new InboundMailPayload(TestData.NewEmail(), "outreach@example.test", "Hi", "Body", null, null, null);

        await using (var off = new PortalFactory(db.ConnectionString))
        {
            var response = await off.Browser().PostAsJsonAsync(TeamEndpoints.InboundMailPath, payload);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        }

        await using var on = new PortalFactory(db.ConnectionString, ("Email:InboundSecret", Secret));
        var client = on.Browser();

        client.DefaultRequestHeaders.Add(TeamEndpoints.InboundTokenHeader, "wrong");
        (await client.PostAsJsonAsync(TeamEndpoints.InboundMailPath, payload)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Remove(TeamEndpoints.InboundTokenHeader);
        client.DefaultRequestHeaders.Add(TeamEndpoints.InboundTokenHeader, Secret);
        (await client.PostAsJsonAsync(TeamEndpoints.InboundMailPath, payload)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Importing_an_official_from_the_directory_makes_an_organization_once()
    {
        var bioguide = $"Z{Guid.NewGuid():N}"[..7];
        await new FederalOfficialsRepository(db.DataSource).UpsertAsync(new FederalOfficial(
            bioguide, "Robin Rivera", "house", "PA", "03", "Democrat", null, "https://rivera.house.gov", null, null));

        await using var portal = new PortalFactory(db.ConnectionString);
        var organizer = portal.Browser();
        await Forms.SignInAsync(organizer, db.DataSource,
            (await TestData.VolunteerAsync(db.DataSource, role: VolunteerRole.Organizer)).Id);

        var first = await Forms.SubmitAsync(organizer, $"{ManyHandsPaths.CrmOfficials}?state=PA", "import", ("Import.BioguideId", bioguide));
        var second = await Forms.SubmitAsync(organizer, $"{ManyHandsPaths.CrmOfficials}?state=PA", "import", ("Import.BioguideId", bioguide));

        first.Headers.Location!.PathAndQuery.Should().Be(second.Headers.Location!.PathAndQuery);
        var org = await Crm.GetOrganizationAsync(Guid.Parse(first.Headers.Location.PathAndQuery.Split('/').Last()));
        org!.Name.Should().Be("Office of Robin Rivera");
        org.ExternalRef.Should().Be(bioguide);
    }
}
