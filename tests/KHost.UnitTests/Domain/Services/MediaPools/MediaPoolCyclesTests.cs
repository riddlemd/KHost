using KHost.Abstractions.Models;
using KHost.Domain.Services.MediaPools;

namespace KHost.UnitTests.Domain.Services.MediaPools;

// A cycle stored in the database is a fault a host cannot see or undo from the page, so it is
// rejected on the way in rather than tolerated by the selector.
public class MediaPoolCyclesTests
{
    private static MediaPool Pool(Guid id, params Guid[] children) => new()
    {
        Id = id,
        Name = "pool",
        Entries = [.. children.Select((c, i) => new MediaPoolEntry { Id = Guid.NewGuid(), ChildPoolId = c, Position = i })],
    };

    [Fact]
    public void CreatesCycle_PoolContainingItself_IsACycle()
    {
        var id = Guid.NewGuid();
        var pool = Pool(id, id);

        Assert.True(MediaPoolCycles.CreatesCycle(pool, new Dictionary<Guid, MediaPool>()));
    }

    [Fact]
    public void CreatesCycle_PoolReachingItselfThroughAChild_IsACycle()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var a = Pool(aId, bId);
        var b = Pool(bId, aId);

        Assert.True(MediaPoolCycles.CreatesCycle(a, new Dictionary<Guid, MediaPool> { [bId] = b }));
    }

    [Fact]
    public void CreatesCycle_ThreeDeepLoop_IsACycle()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var cId = Guid.NewGuid();

        var a = Pool(aId, bId);
        var b = Pool(bId, cId);
        var c = Pool(cId, aId);

        Assert.True(MediaPoolCycles.CreatesCycle(a, new Dictionary<Guid, MediaPool> { [bId] = b, [cId] = c }));
    }

    // Two branches meeting at the same child is a diamond, not a loop — rejecting it would stop a
    // venue putting one shared jingle list inside two others.
    [Fact]
    public void CreatesCycle_TwoBranchesSharingAChild_IsNotACycle()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();
        var cId = Guid.NewGuid();
        var sharedId = Guid.NewGuid();

        var a = Pool(aId, bId, cId);
        var b = Pool(bId, sharedId);
        var c = Pool(cId, sharedId);
        var shared = Pool(sharedId);

        Assert.False(MediaPoolCycles.CreatesCycle(a, new Dictionary<Guid, MediaPool>
        {
            [bId] = b,
            [cId] = c,
            [sharedId] = shared,
        }));
    }

    [Fact]
    public void CreatesCycle_PoolOfPlainTracks_IsNotACycle()
    {
        var pool = new MediaPool
        {
            Id = Guid.NewGuid(),
            Name = "pool",
            Entries = [new MediaPoolEntry { Id = Guid.NewGuid(), MediaId = Guid.NewGuid() }],
        };

        Assert.False(MediaPoolCycles.CreatesCycle(pool, new Dictionary<Guid, MediaPool>()));
    }

    [Fact]
    public void CreatesCycle_ChildThatIsNotLoaded_IsNotACycle()
    {
        var pool = Pool(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(MediaPoolCycles.CreatesCycle(pool, new Dictionary<Guid, MediaPool>()));
    }
}
