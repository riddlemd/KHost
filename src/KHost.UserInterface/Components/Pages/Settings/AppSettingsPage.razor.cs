using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.Domain.Services.VideoEncoding;
using KHost.UserInterface.Models;
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
    [Inject] private TimeProvider Clock { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IMediaSearchService MediaSearchService { get; set; } = default!;
    [Inject] private IBreakMusicService BreakMusic { get; set; } = default!;

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
    private bool _checkingFFmpeg;
    private DateTimeOffset? _ffmpegCheckedAt;

    // The bounds the service clamps to on save, so the control and the store cannot disagree.
    // Qualified: the injected service is also called AppSettings on this page.
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

    private async Task CheckFFmpegAsync()
    {
        _checkingFFmpeg = true;
        try
        {
            _ffmpegStatus = await FFmpeg.CheckAsync();
            _ffmpegCheckedAt = Clock.GetLocalNow();
        }
        finally
        {
            _checkingFFmpeg = false;
        }
    }

    private string CheckSummary(DateTimeOffset checkedAt)
    {
        var missing = new[] { (_ffmpegStatus.FFmpeg, "FFmpeg"), (_ffmpegStatus.FFprobe, "FFprobe") }
            .Where(t => !t.Item1.IsUsable).Select(t => t.Item2).ToList();
        var outcome = missing.Count switch
        {
            0 => "FFmpeg and FFprobe found.",
            1 => $"{missing[0]} missing.",
            _ => "FFmpeg and FFprobe missing.",
        };
        return $"Checked at {checkedAt:T}: {outcome}";
    }

    // Qualified: the injected service is also called AppSettings on this page.
    private static IReadOnlyList<int> LeadInGraceChoices => KHost.UserInterface.Services.AppSettings.LeadInGraceChoices;

    private static string LeadInGraceLabel(int seconds) => seconds == 0 ? "Off" : $"{seconds} seconds";

    private static IReadOnlyList<double> StopFadeChoices => KHost.UserInterface.Services.AppSettings.StopFadeChoices;

    private static string StopFadeLabel(double seconds) => seconds == 0 ? "No fade" : SecondsLabel((int)seconds);

    private static IReadOnlyList<double> BreakMusicFadeChoices => KHost.UserInterface.Services.AppSettings.BreakMusicFadeChoices;

    private static string BreakMusicFadeLabel(double seconds) => seconds switch
    {
        0 => "No fade",
        1 => "1 second",
        _ => $"{seconds:0.#} seconds",
    };

    private static IReadOnlyList<double> AdDurationChoices => KHost.UserInterface.Services.AppSettings.AdDurationChoices;

    private static string AdDurationLabel(double seconds) => $"{seconds:0} seconds";

    // Every whole second the service accepts, so a stored value is always one of them.
    private static IReadOnlyList<int> SegmentSecondsChoices => [.. Enumerable.Range(
        KHost.UserInterface.Services.AppSettings.MinSegmentSeconds,
        KHost.UserInterface.Services.AppSettings.MaxSegmentSeconds - KHost.UserInterface.Services.AppSettings.MinSegmentSeconds + 1)];

    private static string SecondsLabel(int seconds) => seconds == 1 ? "1 second" : $"{seconds} seconds";

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

    /// <summary>Every loaded provider, plus the saved one when it is not loaded: an unmatched select
    /// value renders blank, and saving that would quietly pick something else.</summary>
    private IReadOnlyList<string> BreakMusicChoices
    {
        get
        {
            var loaded = BreakMusic.Providers.Select(provider => provider.SourceName).ToList();

            return AppSettings.Current.BreakMusicProvider is { } saved
                   && !loaded.Contains(saved, StringComparer.OrdinalIgnoreCase)
                ? [saved, .. loaded]
                : loaded;
        }
    }

    /// <summary>The host's own playlists read as such; a plugin's provider names itself.</summary>
    private string BreakMusicLabel(string source)
        => BreakMusic.Providers.FirstOrDefault(p => string.Equals(p.SourceName, source, StringComparison.OrdinalIgnoreCase)) is { } provider
            ? ReferenceEquals(provider, BreakMusic.LibraryProvider) ? $"{provider.DisplayName} playlist" : provider.DisplayName
            : $"{source}: not loaded";

    private static IReadOnlyList<int> GraphicsScaleChoices => GraphicsScaling.Heights;

    private static IReadOnlyList<VideoEncoderPreference> VideoEncoderChoices =>
        [VideoEncoderPreference.Auto, VideoEncoderPreference.Hardware, VideoEncoderPreference.Software];

    private static string VideoEncoderLabel(VideoEncoderPreference preference) => preference.ToString();

    private static IReadOnlyList<VenueBackgrounds> VenueBackgroundsChoices => [VenueBackgrounds.Basic, VenueBackgrounds.Advanced];

    private static string VenueBackgroundsLabel(VenueBackgrounds backgrounds) => $"{backgrounds} Backgrounds";

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
