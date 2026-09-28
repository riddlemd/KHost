using KHost.UserInterface.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace KHost.UnitTests.UserInterface.Services;

public class HostLogLevelTests
{
    [Fact]
    public void Read_WithNothingConfigured_IsInformation()
        => Assert.Equal(LogLevel.Information, HostLogLevel.Read(Configuration(null)));

    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("Warning", LogLevel.Warning)]
    public void Read_TheConventionalKey_IsHonoured(string configured, LogLevel expected)
        => Assert.Equal(expected, HostLogLevel.Read(Configuration(configured)));

    [Theory]
    [InlineData("Loud")]
    [InlineData("42")]
    public void Read_ANameThatIsNoLevel_FallsBackToInformation(string configured)
        => Assert.Equal(LogLevel.Information, HostLogLevel.Read(Configuration(configured)));

    /// <summary>The key the smoke tests set and saw ignored, reaching the host as it does at startup.</summary>
    [Fact]
    public void Read_FromTheEnvironment_IsHonoured()
    {
        const string variable = "KHOST_TEST_Logging__LogLevel__Default";
        Environment.SetEnvironmentVariable(variable, "Debug");
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables("KHOST_TEST_").Build();

            Assert.Equal(LogLevel.Debug, HostLogLevel.Read(configuration));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Read_FromTheCommandLine_IsHonoured()
    {
        var configuration = new ConfigurationBuilder().AddCommandLine(["--Logging:LogLevel:Default=Debug"]).Build();

        Assert.Equal(LogLevel.Debug, HostLogLevel.Read(configuration));
    }

    [Theory]
    [InlineData(LogLevel.Debug, LogLevel.Warning)]
    [InlineData(LogLevel.Information, LogLevel.Warning)]
    [InlineData(LogLevel.Error, LogLevel.Error)]
    public void ForFramework_KeepsTheFrameworkAtWarningUnlessTheHostWantsLess(LogLevel host, LogLevel expected)
        => Assert.Equal(expected, HostLogLevel.ForFramework(host));

    [Theory]
    [InlineData(LogLevel.Trace, LogEventLevel.Verbose)]
    [InlineData(LogLevel.Debug, LogEventLevel.Debug)]
    [InlineData(LogLevel.Information, LogEventLevel.Information)]
    [InlineData(LogLevel.Critical, LogEventLevel.Fatal)]
    public void ToSerilog_MapsEachLevel(LogLevel level, LogEventLevel expected)
        => Assert.Equal(expected, HostLogLevel.ToSerilog(level));

    private static IConfiguration Configuration(string? level)
        => new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(HostLogLevel.ConfigurationKey, level)])
            .Build();
}
