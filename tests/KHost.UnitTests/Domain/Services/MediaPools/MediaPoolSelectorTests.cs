using KHost.Abstractions.Models;
using KHost.Domain.Services.MediaPools;

namespace KHost.UnitTests.Domain.Services.MediaPools;

// The selector is the whole reason a pool is more than a list, so every mode is exercised against
// a seeded Random rather than trusted. A pick that repeats, or a nested pool that never gets
// reached, is silence or the same jingle twice in a row out in the room.
public class MediaPoolSelectorTests
{
    private static readonly Guid _trackA = Guid.NewGuid();
    private static readonly Guid _trackB = Guid.NewGuid();
    private static readonly Guid _trackC = Guid.NewGuid();

    private static MediaPool Pool(
        PoolSelectionMode mode,
        int noRepeat,
        params MediaPoolEntry[] entries) => new()
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            SelectionMode = mode,
            NoRepeatCount = noRepeat,
            Entries = [.. entries],
        };

    private static MediaPoolEntry Track(Guid mediaId, int position = 0, int weight = 1)
        => new() { Id = Guid.NewGuid(), MediaId = mediaId, Position = position, Weight = weight };

    private static MediaPoolEntry Child(Guid poolId, int position = 0, int weight = 1)
        => new() { Id = Guid.NewGuid(), ChildPoolId = poolId, Position = position, Weight = weight };

    private static Guid? Select(MediaPool root, PoolSelectionState state, Random random, params MediaPool[] others)
    {
        var byId = others.ToDictionary(p => p.Id);
        return MediaPoolSelector.SelectNext(root, byId, state, random);
    }

    [Fact]
    public void SelectNext_EmptyPool_ReturnsNull()
    {
        var pool = Pool(PoolSelectionMode.Shuffle, 0);

        Assert.Null(Select(pool, new PoolSelectionState(), new Random(1)));
    }

    [Fact]
    public void SelectNext_Sequential_WalksEntriesInPositionOrder()
    {
        var pool = Pool(PoolSelectionMode.Sequential, 0,
            Track(_trackB, position: 1), Track(_trackA, position: 0), Track(_trackC, position: 2));

        var state = new PoolSelectionState();
        var random = new Random(1);

        Assert.Equal(_trackA, Select(pool, state, random));
        Assert.Equal(_trackB, Select(pool, state, random));
        Assert.Equal(_trackC, Select(pool, state, random));
    }

    [Fact]
    public void SelectNext_Sequential_WrapsAtTheEnd()
    {
        var pool = Pool(PoolSelectionMode.Sequential, 0,
            Track(_trackA, position: 0), Track(_trackB, position: 1));

        var state = new PoolSelectionState();
        var random = new Random(1);

        Select(pool, state, random);
        Select(pool, state, random);

        Assert.Equal(_trackA, Select(pool, state, random));
    }

    [Fact]
    public void SelectNext_Weighted_FavoursTheHeavierEntry()
    {
        var pool = Pool(PoolSelectionMode.Weighted, 0,
            Track(_trackA, weight: 9), Track(_trackB, weight: 1));

        var state = new PoolSelectionState();
        var random = new Random(20260824);

        var heavy = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (Select(pool, state, random) == _trackA)
                heavy++;
        }

        // A 9:1 split lands near 900; the band is wide enough to survive the seed changing but
        // narrow enough that ignoring weight entirely (500) fails.
        Assert.InRange(heavy, 820, 960);
    }

    [Fact]
    public void SelectNext_Weighted_ZeroWeightEntryIsNeverPicked()
    {
        var pool = Pool(PoolSelectionMode.Weighted, 0,
            Track(_trackA, weight: 1), Track(_trackB, weight: 0));

        var state = new PoolSelectionState();
        var random = new Random(7);

        for (var i = 0; i < 200; i++)
            Assert.Equal(_trackA, Select(pool, state, random));
    }

    [Fact]
    public void SelectNext_WeightedWithEveryWeightZero_ReturnsNull()
    {
        var pool = Pool(PoolSelectionMode.Weighted, 0,
            Track(_trackA, weight: 0), Track(_trackB, weight: 0));

        Assert.Null(Select(pool, new PoolSelectionState(), new Random(1)));
    }

    // Drawn repeatedly rather than twice: with two tracks and a shuffle, one pair of picks differs
    // half the time by luck alone, so a two-call version passes with the window switched off.
    [Fact]
    public void SelectNext_NoRepeatWindowOfOne_NeverPlaysTheSameTrackTwiceRunning()
    {
        var pool = Pool(PoolSelectionMode.Shuffle, noRepeat: 1, Track(_trackA), Track(_trackB));

        var state = new PoolSelectionState();
        var random = new Random(3);

        Guid? previous = null;
        for (var i = 0; i < 50; i++)
        {
            var pick = Select(pool, state, random);

            Assert.NotEqual(previous, pick);
            previous = pick;
        }
    }

    [Fact]
    public void SelectNext_NoRepeatWindowOfTwo_KeepsATrackOutForTwoFurtherPicks()
    {
        var pool = Pool(PoolSelectionMode.Shuffle, noRepeat: 2,
            Track(_trackA), Track(_trackB), Track(_trackC));

        var state = new PoolSelectionState();
        var random = new Random(11);

        var picks = new List<Guid?>();
        for (var i = 0; i < 30; i++)
            picks.Add(Select(pool, state, random));

        for (var i = 2; i < picks.Count; i++)
            Assert.DoesNotContain(picks[i], picks.GetRange(i - 2, 2));
    }

    // A pool smaller than its own no-repeat window would have nothing eligible, and silence in
    // the room is worse than a repeat — so the window is relaxed rather than obeyed.
    [Fact]
    public void SelectNext_PoolSmallerThanItsNoRepeatWindow_StillPlays()
    {
        var pool = Pool(PoolSelectionMode.Shuffle, noRepeat: 5, Track(_trackA));

        var state = new PoolSelectionState();
        var random = new Random(4);

        Assert.Equal(_trackA, Select(pool, state, random));
        Assert.Equal(_trackA, Select(pool, state, random));
    }

    [Fact]
    public void SelectNext_NestedPool_ReturnsATrackFromTheChild()
    {
        var child = Pool(PoolSelectionMode.Sequential, 0, Track(_trackC));
        var root = Pool(PoolSelectionMode.Sequential, 0, Child(child.Id));

        Assert.Equal(_trackC, Select(root, new PoolSelectionState(), new Random(1), child));
    }

    // The child's own mode governs inside it, which is the point of nesting: a weighted root can
    // hold a sequential sub-playlist without either knowing about the other.
    [Fact]
    public void SelectNext_NestedPool_UsesTheChildsOwnSelectionMode()
    {
        var child = Pool(PoolSelectionMode.Sequential, 0,
            Track(_trackA, position: 0), Track(_trackB, position: 1));
        var root = Pool(PoolSelectionMode.Weighted, 0, Child(child.Id, weight: 1));

        var state = new PoolSelectionState();
        var random = new Random(5);

        Assert.Equal(_trackA, Select(root, state, random, child));
        Assert.Equal(_trackB, Select(root, state, random, child));
    }

    [Fact]
    public void SelectNext_EntryPointingAtAMissingPool_IsSkipped()
    {
        var root = Pool(PoolSelectionMode.Sequential, 0,
            Child(Guid.NewGuid(), position: 0), Track(_trackA, position: 1));

        Assert.Equal(_trackA, Select(root, new PoolSelectionState(), new Random(1)));
    }

    [Fact]
    public void SelectNext_EmptyNestedPool_FallsBackToASiblingTrack()
    {
        var empty = Pool(PoolSelectionMode.Shuffle, 0);
        var root = Pool(PoolSelectionMode.Sequential, 0,
            Child(empty.Id, position: 0), Track(_trackA, position: 1));

        Assert.Equal(_trackA, Select(root, new PoolSelectionState(), new Random(1), empty));
    }

    // Saving rejects a cycle, so this only happens to rows edited outside the app — but it must
    // terminate rather than hang the console mid-show.
    [Fact]
    public void SelectNext_PoolThatReachesItself_TerminatesAndReturnsTheReachableTrack()
    {
        var a = Pool(PoolSelectionMode.Sequential, 0);
        var b = Pool(PoolSelectionMode.Sequential, 0);

        a.Entries = [Child(b.Id, position: 0), Track(_trackA, position: 1)];
        b.Entries = [Child(a.Id, position: 0)];

        Assert.Equal(_trackA, Select(a, new PoolSelectionState(), new Random(1), b));
    }

    [Fact]
    public void SelectNext_TwoPoolsSharingAChild_ReachesItFromBothBranches()
    {
        var shared = Pool(PoolSelectionMode.Sequential, 0, Track(_trackC));
        var left = Pool(PoolSelectionMode.Sequential, 0, Child(shared.Id));
        var right = Pool(PoolSelectionMode.Sequential, 0, Child(shared.Id));
        var root = Pool(PoolSelectionMode.Sequential, 0,
            Child(left.Id, position: 0), Child(right.Id, position: 1));

        var state = new PoolSelectionState();
        var random = new Random(1);

        // The second descent must not mistake the shared pool for a cycle just because the first
        // branch already walked through it.
        Assert.Equal(_trackC, Select(root, state, random, left, right, shared));
        Assert.Equal(_trackC, Select(root, state, random, left, right, shared));
    }

    [Fact]
    public void SelectNext_RecordsThePickInHistory()
    {
        var pool = Pool(PoolSelectionMode.Sequential, noRepeat: 3, Track(_trackA));
        var state = new PoolSelectionState();

        Select(pool, state, new Random(1));

        Assert.Equal(_trackA, Assert.Single(state.Recent));
    }
}
