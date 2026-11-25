using FluentAssertions;
using ManyHands.Core;
using Website.Portal.Volunteer;

namespace Website.Tests;

/// <summary>
/// The organizers' sidebar (TeamNav): its groups, and that every team page,
/// a record's own page included, says which place it belongs to.
/// </summary>
public class TeamNavTests
{
    [Fact]
    public void The_sidebar_is_volunteering_lobbying_partners_mail_then_the_site()
    {
        TeamNav.Sections.Select(s => s.Heading).Should().Equal("Volunteering", "Lobbying", "Partners", "Mail", "Site");
        TeamNav.Sections[0].Links.Select(l => l.Label).Should().Equal("Activities", "Members", "Invites");
        TeamNav.Sections[1].Links.Select(l => l.Label).Should().StartWith(["Organizations", "Pipeline", "Officials"]);
        TeamNav.Sections[2].Links.Select(l => l.Label).Should().Equal("Partners", "Import");
        TeamNav.Sections[3].Links.Select(l => l.Label).Should().Equal("Inbox", "Write", "Templates");
        TeamNav.Sections[^1].Links.Should().BeSameAs(TeamNav.SiteLinks);
    }

    [Fact]
    public void Every_icon_in_the_sidebar_is_in_the_font_it_loads() =>
        TeamNav.Sections.SelectMany(s => s.Links).Select(l => l.Icon).Should().BeSubsetOf(Symbols.All);

    [Theory]
    [InlineData(ManyHandsPaths.Team, "Activities")]
    [InlineData(ManyHandsPaths.Team + "/", "Activities")]
    [InlineData(ManyHandsPaths.TeamNewActivity, "Activities")]
    [InlineData(ManyHandsPaths.TeamMembers + "?page=2", "Members")]
    [InlineData(ManyHandsPaths.TeamInvites, "Invites")]
    [InlineData(ManyHandsPaths.Crm + "?kind=Venue", "Organizations")]
    [InlineData(ManyHandsPaths.CrmPipeline, "Pipeline")]
    [InlineData(ManyHandsPaths.CrmOfficials, "Officials")]
    [InlineData(ManyHandsPaths.TeamPartners, "Partners")]
    [InlineData(ManyHandsPaths.TeamPartnersImport, "Import")]
    [InlineData(ManyHandsPaths.Inbox, "Inbox")]
    [InlineData(ManyHandsPaths.InboxCompose, "Write")]
    [InlineData(ManyHandsPaths.InboxTemplates, "Templates")]
    public void A_page_marks_its_place(string path, string label) =>
        TeamNav.Current(path)!.Label.Should().Be(label);

    [Fact]
    public void A_records_own_page_marks_the_place_it_belongs_to()
    {
        var id = Guid.NewGuid();

        TeamNav.Current(ManyHandsPaths.TeamActivity(id))!.Label.Should().Be("Activities");
        TeamNav.Current(ManyHandsPaths.TeamEditActivity(id))!.Label.Should().Be("Activities");
        TeamNav.Current(ManyHandsPaths.TeamCheckInCode(id))!.Label.Should().Be("Activities");
        TeamNav.Current(ManyHandsPaths.CrmOrganization(id))!.Label.Should().Be("Organizations");
        TeamNav.Current(ManyHandsPaths.CrmEngagement(id))!.Label.Should().Be("Pipeline");
        TeamNav.Current(ManyHandsPaths.InboxThread(id))!.Label.Should().Be("Inbox");
    }

    [Fact]
    public void A_page_under_no_place_marks_none() =>
        TeamNav.Current("/team/nothing-here").Should().BeNull();
}
