using KHost.Abstractions.Models;

namespace KHost.UnitTests.Domain.Models;

public class KHostUserTests
{
    [Fact]
    public void KHostUser_InitializesWithNewGuidId()
    {
        var user = new KHostUser { Name = "Alice" };

        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public void KHostUser_TwoInstancesHaveDistinctIds()
    {
        var a = new KHostUser { Name = "Alice" };
        var b = new KHostUser { Name = "Bob" };

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void KHostUser_DefaultGroupsIsEmpty()
    {
        var user = new KHostUser { Name = "Alice" };

        Assert.Empty(user.Groups);
    }

    [Fact]
    public void KHostUser_NotesDefaultsToEmptyString()
    {
        var user = new KHostUser { Name = "Alice" };

        Assert.Equal("", user.Notes);
    }

    [Fact]
    public void KHostUser_PasswordHashDefaultsToNull()
    {
        var user = new KHostUser { Name = "Alice" };

        Assert.Null(user.PasswordHash);
    }
}
