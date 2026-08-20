using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.UserInterface.Auth;

namespace KHost.UnitTests.UserInterface.Auth;

public class PasswordResetTests
{
    private readonly IUsersRepository _users = Substitute.For<IUsersRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly StringWriter _output = new();

    public PasswordResetTests()
        => _hasher.HashAsync(Arg.Any<string>()).Returns(call => Task.FromResult("hashed:" + call.Arg<string>()));

    [Fact]
    public async Task RunAsync_StoresAHashOfTheGeneratedPassword()
    {
        var user = new KHostUser { Name = "Admin" };
        _users.FindByNameAsync("Admin").Returns(user);

        var exitCode = await PasswordReset.RunAsync("Admin", _users, _hasher, _output);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("hashed:", user.PasswordHash);
        await _users.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task RunAsync_PrintsAReadablePassword_ThatMatchesTheStoredHash()
    {
        var user = new KHostUser { Name = "Admin" };
        _users.FindByNameAsync("Admin").Returns(user);

        await PasswordReset.RunAsync("Admin", _users, _hasher, _output);

        var printed = _output.ToString().Split("is now: ")[1].Split('\n')[0].Trim();
        Assert.Equal(12, printed.Length);
        Assert.DoesNotMatch("[0O1lI]", printed);
        Assert.Equal("hashed:" + printed, user.PasswordHash);
    }

    [Fact]
    public async Task RunAsync_RefusesAnUnknownUser_WithoutWritingAnything()
    {
        _users.FindByNameAsync("Nobody").Returns((KHostUser?)null);

        var exitCode = await PasswordReset.RunAsync("Nobody", _users, _hasher, _output);

        Assert.Equal(1, exitCode);
        Assert.Contains("No user named 'Nobody'", _output.ToString());
        await _users.DidNotReceive().UpdateAsync(Arg.Any<KHostUser>());
    }
}
