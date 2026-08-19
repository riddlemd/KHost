using System.Reflection;
using KHost.Abstractions.Exceptions;
using KHost.UserInterface.Components;
using KHost.UserInterface.Services;
using NSubstitute;

namespace KHost.UnitTests.UserInterface.Components;

public class KHostErrorBoundaryTests
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly KHostErrorBoundary _boundary = new();

    public KHostErrorBoundaryTests()
        => typeof(KHostErrorBoundary)
            .GetProperty("DialogService", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_boundary, _dialogs);

    [Fact]
    public async Task APresentableFailure_BecomesADialog()
    {
        var error = new KHostException("The file went missing.", "Check the drive.", "KH-TEST");

        await InvokeOnErrorAsync(error);

        await _dialogs.Received(1).ShowErrorAsync(error, Arg.Any<string>(), Arg.Any<Action?>(), Arg.Any<Action?>());
    }

    /// <summary>OnErrorAsync is protected; the boundary is only ever driven by the framework.</summary>
    private Task InvokeOnErrorAsync(Exception exception)
    {
        var method = typeof(KHostErrorBoundary)
            .GetMethod("OnErrorAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (Task)method.Invoke(_boundary, [exception])!;
    }
}
