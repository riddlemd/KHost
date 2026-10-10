using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

/// <summary>The lyrics offset App Setting in the Now Playing header, tuned 10 ms a click while a song
/// plays, shown only when App Settings turns it on and the song has timed words.</summary>
/// <remarks>Saved through the same settings as the App Settings page, so the two never disagree.
/// Clicks are not held back for their saves: each moves the value at once, and the saves run one at
/// a time with whatever value is newest, so a burst ends on the last click.</remarks>
public partial class LyricsOffsetStepper : IDisposable
{
    internal const int Step = 10;

    /// <summary>Whether the loaded song has words KHost draws or burns in, the only words an offset moves.</summary>
    [Parameter] public bool SongHasTimedLyrics { get; set; }

    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IMessageBroker Broker { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private bool _shown;
    private int _offset;

    private string Label => _offset switch
    {
        0 => "0 ms",
        > 0 => $"+{_offset} ms",
        _ => $"−{-_offset} ms",
    };

    private string Title => _offset switch
    {
        0 => "Lyrics offset: none",
        > 0 => $"Words shown {_offset} ms later than the music — click to set back to 0",
        _ => $"Words shown {-_offset} ms earlier than the music — click to set back to 0",
    };

    protected override void OnInitialized()
    {
        ReadSaved();

        _subscriptions.Add(Broker.Subscribe<TimedLyricsSettingsChanged>(_ => InvokeAsync(() =>
        {
            // A burst still saving keeps its own newest value rather than jumping back to an older save.
            if (_saveLock.CurrentCount == 0) return;

            ReadSaved();
            StateHasChanged();
        })));
    }

    private void ReadSaved()
    {
        var settings = AppSettings.Current;
        _shown = settings?.ShowLyricsOffsetControl ?? false;
        _offset = LyricsOffset.ClampMilliseconds(settings?.LyricsOffsetMilliseconds ?? 0);
    }

    private Task MoveAsync(int by)
    {
        _offset = LyricsOffset.ClampMilliseconds(_offset + by);
        return SaveAsync();
    }

    private Task ResetAsync()
    {
        _offset = 0;
        return SaveAsync();
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            // The whole snapshot goes back, as the settings page sends it, with this one value moved.
            if (AppSettings.Current is not { } settings || settings.LyricsOffsetMilliseconds == _offset) return;

            settings.LyricsOffsetMilliseconds = _offset;

            var result = await AppSettings.SaveAsync(settings);

            if (!result.Saved)
            {
                Flash.Show(result.Error ?? "The lyrics offset could not be saved.", FlashType.Warning);
                ReadSaved();
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
        _saveLock.Dispose();
    }
}
