using KHost.UserInterface.Startup;
using Oriel;

namespace KHost.UnitTests.UserInterface.Startup;

public sealed class NativeShellTests
{
    [Fact]
    public void Given_APageThatTitlesItself_When_TheConsoleWindowHearsTheNewTitle_Then_TheWindowKeepsItsOwn()
    {
        // Given
        var update = new PageTitleUpdatedEventArgs("KHost: Hosting Dashboard");

        // When
        NativeShell.KeepAppTitle(null, update);

        // Then
        Assert.True(update.Cancel);
    }
}
