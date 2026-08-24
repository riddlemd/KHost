using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>
/// A single file, added deliberately. The folder importer stays karaoke-only on purpose — pointed
/// at a music folder it would sweep every piece of album art in as a song.
/// </summary>
public partial class AddMediaFileDialog
{
    [Inject] private IMediaFileParsingService Parser { get; set; } = default!;
    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IMediaRepository MediaRepository { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback OnAdded { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private string _path = "";
    private ImageScaling _scaling = ImageScaling.Fit;

    /// <summary>Driven off the path as it is typed, so the option appears only for a still.</summary>
    private bool _isImage => MediaFormats.IsImage(Path.GetExtension(_path.Trim().Trim('"')));

    private MediaType _type = MediaType.Audio;
    private string? _error;
    private bool _busy;
    private bool _prevIsOpen;

    protected override void OnParametersSet()
    {
        if (IsOpen && !_prevIsOpen)
        {
            _path = "";
            _type = MediaType.Audio;
            _scaling = ImageScaling.Fit;
            _error = null;
            _busy = false;
        }

        _prevIsOpen = IsOpen;
    }

    private async Task AddAsync()
    {
        _error = null;

        var path = _path.Trim().Trim('"');

        if (path.Length == 0)
        {
            _error = "Enter the full path to a file.";
            return;
        }

        if (!File.Exists(path))
        {
            _error = "No file at that path.";
            return;
        }

        // FilePath is unique across every type, so this catches a file already in the library as a
        // song rather than letting the insert die on the index.
        if (await MediaRepository.FindByFilePathAsync(path) is not null)
        {
            _error = "That file is already in the library.";
            return;
        }

        // A backing track has no singer on it and is often not the original recording, so it is a
        // song to queue and nothing else. The .cdg beside it is what gives it away.
        if (_type != MediaType.Karaoke && MediaFormats.IsKaraokeTrack(path))
        {
            _error = "That is a karaoke backing track, so it can only be added as karaoke.";
            return;
        }

        _busy = true;

        try
        {
            var media = await Parser.LoadAndParseAsync(path);
            media.Type = _type;
            media.ImageScaling = _scaling;

            await Media.CreateAsync(media);

            await OnAdded.InvokeAsync();
            await OnClose.InvokeAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
