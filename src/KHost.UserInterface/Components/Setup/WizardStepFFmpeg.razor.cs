using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Setup;

/// <summary>Finds FFmpeg, and installs it without being asked when it is missing: a first run has
/// no songs to play without it, so offering a button would only add a click.</summary>
public partial class WizardStepFFmpeg : IDisposable
{
    [Inject] private IFFmpegService FFmpeg { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    [Parameter] public EventCallback OnComplete { get; set; }

    private readonly SubscriptionSet _subscriptions = new();

    private FFmpegStatus _status = default!;

    // Set from the moment an install is decided on, so the gap before the service reports it
    // running never shows Retry.
    private bool _installing;

    private bool Busy => !_status.HasChecked || _installing || _status.Install.IsRunning;

    private bool Failed => !_status.IsReady && !Busy;

    protected override async Task OnInitializedAsync()
    {
        _status = FFmpeg.Status;

        _subscriptions.Add(Broker.Subscribe<FFmpegChanged>(changed =>
        {
            _status = FFmpeg.Status;
            _ = InvokeAsync(StateHasChanged);
        }));

        _status = await FFmpeg.CheckAsync();

        // Not awaited: the page has to paint the progress while the download runs.
        if (!_status.IsReady && FFmpeg.CanInstall)
        {
            _installing = true;
            _ = InvokeAsync(InstallAsync);
        }
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
