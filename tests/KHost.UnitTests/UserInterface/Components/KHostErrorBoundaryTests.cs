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

    /// <summary>
    /// A console runs a room. Replacing the page with error content because a settings page threw
    /// loses the queue and the now-playing panel, which is worse than the failure that caused it.
    /// </summary>
    [Fact]
    public async Task AnUnexpectedFailure_AlsoBecomesADialog()
    {
        await InvokeOnErrorAsync(new InvalidOperationException("something internal"));

        await _dialogs.Received(1).ShowErrorAsync(
            Arg.Any<KHostException>(), Arg.Any<string>(), Arg.Any<Action?>(), Arg.Any<Action?>());
    }

    // The type is in the reference code because that is the only part of an unexpected failure a
    // host can quote back, and every one of them shares the same wording.
    [Fact]
    public async Task AnUnexpectedFailure_IsQuotableAndKeepsTheOriginal()
    {
        var original = new InvalidOperationException("something internal");

        await InvokeOnErrorAsync(original);

        var shown = _dialogs.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IDialogService.ShowErrorAsync))
            .GetArguments()[0] as KHostException;

        Assert.Equal("KH-UNEXPECTED-InvalidOperationException", shown?.ReferenceCode);
        Assert.Same(original, shown?.InnerException);
    }

    [Fact]
    public async Task APresentableFailure_IsPassedThroughUnchanged()
    {
        var error = new KHostException("The file went missing.", "Check the drive.", "KH-TEST");

        await InvokeOnErrorAsync(error);

        var shown = _dialogs.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IDialogService.ShowErrorAsync))
            .GetArguments()[0];

        Assert.Same(error, shown);
    }

    /// <summary>OnErrorAsync is protected; the boundary is only ever driven by the framework.</summary>
    private Task InvokeOnErrorAsync(Exception exception)
    {
        var method = typeof(KHostErrorBoundary)
            .GetMethod("OnErrorAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (Task)method.Invoke(_boundary, [exception])!;
    }
}
