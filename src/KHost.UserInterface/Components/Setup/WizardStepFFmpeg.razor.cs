using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Setup;

/// <summary>Finds FFmpeg, and waits for the host to ask before downloading it: the download is a
/// GPL-licensed third-party build, so starting it unasked would not be consent.</summary>
public partial class WizardStepFFmpeg : IDisposable
{
    [Inject] private IFFmpegService FFmpeg { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    [Parameter] public EventCallback OnComplete { get; set; }

    private readonly SubscriptionSet _subscriptions = new();

    private FFmpegStatus _status = default!;

    // Set from the moment an install is decided on, so the gap before the service reports it
    // running never shows Download/Retry.
    private bool _installing;

    private bool Busy => !_status.HasChecked || _installing || _status.Install.IsRunning;

    private bool Failed => !_status.IsReady && !Busy && _status.Install.State == FFmpegInstallState.Failed;

    private bool NotStarted => !_status.IsReady && !Busy && _status.Install.State != FFmpegInstallState.Failed;

    protected override async Task OnInitializedAsync()
    {
        _status = FFmpeg.Status;

        _subscriptions.Add(Broker.Subscribe<FFmpegChanged>(changed =>
        {
            _status = FFmpeg.Status;
            _ = InvokeAsync(StateHasChanged);
        }));

        _status = await FFmpeg.CheckAsync();
    }

    public void Dispose() => _subscriptions.Dispose();

    private async Task InstallAsync()
    {
        _installing = true;

        try
        {
            _status = await FFmpeg.InstallAsync();
        }
        finally
        {
            _installing = false;
            StateHasChanged();
        }
    }

    private Task OnNextAsync() => OnComplete.InvokeAsync();
}
