using KHost.UserInterface.Services;

namespace KHost.UnitTests.UserInterface.Services;

public class LiveLogServiceTests
{
    [Fact]
    public void BuildArguments_PathWithAnApostrophe_DoublesItInsideTheSingleQuotes()
    {
        var arguments = LiveLogService.BuildArguments(@"C:\Users\O'Neil\KHost\logs\host-20261003-120000.log");

        Assert.Contains(@"-LiteralPath 'C:\Users\O''Neil\KHost\logs\host-20261003-120000.log'", arguments);
    }

    [Fact]
    public void BuildArguments_Always_FollowsTheFileAsUtf8()
    {
        var arguments = LiveLogService.BuildArguments(@"C:\logs\host.log");

        Assert.Contains("-Encoding UTF8 -Tail 200 -Wait", arguments);
    }
}
