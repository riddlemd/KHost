using KHost.Abstractions.Models;
using KHost.UserInterface.Components.Panels;

namespace KHost.UnitTests.UserInterface.Components.Panels;

public class SingerQueuePanelRankingTests
{
    private static readonly Guid Here = Guid.NewGuid();
    private static readonly Guid Anchor = Guid.NewGuid();
    private static readonly Guid Kellys = Guid.NewGuid();

    private static readonly DateTime Now = new(2026, 8, 21, 20, 0, 0, DateTimeKind.Utc);

    private readonly Dictionary<Guid, RecentVenueVisit> _visits = [];

    [Fact]
    public void TheCurrentVenuesSingersComeFirst_EvenWhenOthersSangMoreRecently()
    {
        var stale = Singer("Here but stale", Here, Now.AddDays(-30));
        var fresh = Singer("Elsewhere yesterday", Anchor, Now.AddDays(-1));

        Assert.Equal(["Here but stale", "Elsewhere yesterday"], Rank(fresh, stale));
    }

    [Fact]
    public void InsideAVenue_TheMostRecentSingerIsFirst()
    {
        var older = Singer("Older", Here, Now.AddDays(-9));
        var newer = Singer("Newer", Here, Now.AddDays(-2));

        Assert.Equal(["Newer", "Older"], Rank(older, newer));
    }

    /// <summary>
    /// Groups have to stay whole, or the menu labels a run that is one singer long and then labels
    /// the same venue again further down.
    /// </summary>
    [Fact]
    public void OtherVenuesStayTogether_OrderedByTheirMostRecentSinger()
    {
        var anchorOld = Singer("Anchor old", Anchor, Now.AddDays(-40));
        var anchorNew = Singer("Anchor new", Anchor, Now.AddDays(-3));
        var kellys = Singer("Kellys", Kellys, Now.AddDays(-10));

        Assert.Equal(["Anchor new", "Anchor old", "Kellys"], Rank(kellys, anchorOld, anchorNew));
    }

    [Fact]
    public void SingersWithNoVenueAtAll_GoLast()
    {
        var unknown = Unknown("Never sung");
        var elsewhere = Singer("Elsewhere", Anchor, Now.AddYears(-2));

        Assert.Equal(["Elsewhere", "Never sung"], Rank(unknown, elsewhere));
    }

    [Fact]
    public void SingersWithNoVenueAtAll_AreOrderedByName()
    {
        Assert.Equal(["Aaron", "Zoe"], Rank(Unknown("Zoe"), Unknown("Aaron")));
    }

    [Fact]
    public void WithNoVenueSelected_NoGroupIsPromoted()
    {
        var anchor = Singer("Anchor", Anchor, Now.AddDays(-1));
        var here = Singer("Here", Here, Now.AddDays(-5));

        Assert.Equal(["Anchor", "Here"], Rank(currentVenueId: null, here, anchor));
    }

    private KHostUser Singer(string name, Guid venueId, DateTime lastSungOn)
    {
        var singer = new KHostUser { Id = Guid.NewGuid(), Name = name };
        _visits[singer.Id] = new RecentVenueVisit(venueId, lastSungOn);
        return singer;
    }

    private static KHostUser Unknown(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private string[] Rank(params KHostUser[] singers) => Rank(Here, singers);

    private string[] Rank(Guid? currentVenueId, params KHostUser[] singers)
        => [.. SingerQueuePanel.RankByVenue(singers, _visits, currentVenueId).Select(s => s.Name)];
}
