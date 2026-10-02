using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;


namespace KHost.UserInterface.Components.Pages.Settings;

public partial class AppSettingsPage : IDisposable
{
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;
    [Inject] private IDialogService Dialog { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IFFmpegService FFmpeg { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IMediaSearchService MediaSearchService { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    private IDisposable? _navigationGuard;

    // Set while leaving on the host's answer, so the guard does not stop the navigation it asked for.
    private bool _leaving;

    private AppSettings _model = new();
    private bool _restartRequired;
    private bool _saving;
    private string? _error;
    private string? _defaultMediaDirectory;
    private FFmpegStatus _ffmpegStatus = default!;

    // The bounds the service clamps to on save, so the control and the store cannot disagree.
    // Qualified: the injected service is also called AppSettings on this page.
    private const double MinStopFadeSeconds = KHost.UserInterface.Services.AppSettings.MinStopFadeSeconds;
    private const double MaxStopFadeSeconds = KHost.UserInterface.Services.AppSettings.MaxStopFadeSeconds;
    private const int MinSegmentSeconds = KHost.UserInterface.Services.AppSettings.MinSegmentSeconds;
    private const int MaxSegmentSeconds = KHost.UserInterface.Services.AppSettings.MaxSegmentSeconds;
    private const double MinAdDurationSeconds = KHost.UserInterface.Services.AppSettings.MinAdDurationSeconds;
    private const double MaxAdDurationSeconds = KHost.UserInterface.Services.AppSettings.MaxAdDurationSeconds;
    private const int MinPageSize = KHost.UserInterface.Services.AppSettings.MinPageSize;
    private const int MaxPageSize = KHost.UserInterface.Services.AppSettings.MaxPageSize;

    protected override void OnInitialized()
    {
        _model = AppSettings.Current;
        _restartRequired = AppSettings.RestartRequired;
        _defaultMediaDirectory = AppSettings.DefaultMediaDirectory;
        _ffmpegStatus = FFmpeg.Status;

        _subscriptions.Add(Broker.Subscribe<FFmpegChanged>(changed =>
        {
            _ffmpegStatus = FFmpeg.Status;
            _ = InvokeAsync(StateHasChanged);
        }));

        // Registered here rather than on first render: a navigation can be asked for before the
        // page has painted, and an unguarded one loses the edits without a word.
        _navigationGuard = Navigation.RegisterLocationChangingHandler(OnLocationChangingAsync);
    }

    /// <summary>Compares to what is stored, so a field edited and put back reads as not dirty.</summary>
    private bool HasUnsavedChanges => _model != AppSettings.Current;

    private async ValueTask OnLocationChangingAsync(LocationChangingContext context)
    {
        if (_leaving || !HasUnsavedChanges) return;

        // Held rather than cancelled: the host has not chosen yet, and the target has to survive
        // long enough to be navigated to once they do.
        context.PreventNavigation();

        var target = context.TargetLocation;

        await Dialog.ShowUnsavedChangesAsync(
            onSave: async () =>
            {
                await SaveAsync();

                // A refused save keeps the host here with the reason on screen, rather than
                // carrying them away from settings that did not take.
                if (_error is null) LeaveTo(target);
            },
            onDiscard: () =>
            {
                LeaveTo(target);
                return Task.CompletedTask;
            });
    }

    private void LeaveTo(string target)
    {
        _leaving = true;
        Navigation.NavigateTo(target);
    }

    public void Dispose()
    {
        _navigationGuard?.Dispose();
        _subscriptions.Dispose();
    }

    private async Task InstallFFmpegAsync()
    {
        _ffmpegStatus = await FFmpeg.InstallAsync();

        if (_ffmpegStatus.Install.State == FFmpegInstallState.Succeeded)
            Flash.Show("FFmpeg installed. The next song uses it.");
    }

    private async Task CheckFFmpegAsync() => _ffmpegStatus = await FFmpeg.CheckAsync();

    // Qualified: the injected service is also called AppSettings on this page.
    private static IReadOnlyList<int> LeadInGraceChoices => KHost.UserInterface.Services.AppSettings.LeadInGraceChoices;

    private static string LeadInGraceLabel(int seconds) => seconds == 0 ? "Off" : $"{seconds} seconds";

    private static IReadOnlyList<int> DynamicLeadInPauseChoices => KHost.UserInterface.Services.AppSettings.DynamicLeadInPauseChoices;

    private static string DynamicLeadInPauseLabel(int seconds) => seconds == 1 ? "1 second" : $"{seconds} seconds";

    /// <summary>"Remember" plus every mode the search panel itself offers — Local included — so the
    /// setting never disagrees with what the panel's own dropdown shows.</summary>
    private IReadOnlyList<string> SearchModeChoices => [
        KHost.UserInterface.Services.AppSettings.RememberLastSearchMode,
        .. MediaSearchService.Providers.Select(provider => provider.SourceName),
    ];

    private string SearchModeLabel(string mode) =>
        mode == KHost.UserInterface.Services.AppSettings.RememberLastSearchMode
            ? "Remember the last one used"
            : MediaSearchService.Providers.FirstOrDefault(provider => provider.SourceName == mode)?.DisplayName ?? mode;

    private static IReadOnlyList<int> GraphicsScaleChoices => GraphicsScaling.Heights;

    private static string GraphicsScaleLabel(int height) => height switch
    {
        GraphicsScaling.Off => "Off",
        2160 => "4K",
        _ => $"{height}p",
    };

    private async Task SaveAsync()
    {
        _saving = true;
        _error = null;

        var result = await AppSettings.SaveAsync(_model);

        if (result.Saved)
        {
            Flash.Show("App settings saved.");
        }
        else
        {
            _error = result.Error;
            Flash.Show(_error ?? "App settings were not saved.", FlashType.Warning);
            // The refused toggle must not keep looking flipped.
            _model = AppSettings.Current;
        }

        _restartRequired = AppSettings.RestartRequired;
        _saving = false;
    }
}
