using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Display;
using KHost.Domain.Services.Displays.LocalScreen;
using KHost.Domain.Services.Visualisations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components;

/// <summary>Edits what one visualisation draws and how: its preset or video, brightness, colour,
/// sensitivity, bars and palette, with a preview beside it.</summary>
/// <remarks>Saves nothing. Changes <see cref="Look"/> in place and raises <see cref="LookChanged"/>
/// once a change is made; a slider being dragged moves <see cref="Look"/> and the preview only, and
/// raises it when let go.</remarks>
public partial class VisualisationLookEditor
{
    /// <summary>The select's value for a video, whichever video it names.</summary>
    internal static readonly string VideoKey = PresetKey(VisualiserPresetSource.Video, "");

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IVideoBackdropService Backdrops { get; set; } = default!;
    [Inject] private IServiceProvider Services { get; set; } = default!;

    // The selected venue's theme, so a look that respects it previews in it; null with no venue or no theme.
    private IReadOnlyList<string>? _venuePalette;

    /// <summary>The preview's answer for one video: whether its row is there, and where it plays as
    /// it is, or null when it would need encoding. Asked once per video picked, since asking reads
    /// the file.</summary>
    private (Guid MediaId, bool Found, string? Url)? _previewVideo;

    private ElementReference _preview;

    /// <summary>What the preview was last told, so a render that changed nothing sends nothing.</summary>
    private string? _previewSent;

    [Parameter, EditorRequired] public IVisualisationLook Look { get; set; } = default!;

    /// <summary>Every preset on offer.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<VisualiserPreset> Presets { get; set; } = [];

    [Parameter] public EventCallback<IVisualisationLook> LookChanged { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ShowPreview { get; set; } = true;

    private bool IsAvailable
        => Look.PresetSource == VisualiserPresetSource.Video || Presets.Any(p => p.Source == Look.PresetSource && p.Name == Look.PresetName);

    /// <summary>Whether the picked video is there but the preview cannot play it as it is.</summary>
    private bool PreviewNeedsEncode
        => Look is { PresetSource: VisualiserPresetSource.Video, VideoMediaId: { } id }
           && _previewVideo is { Found: true, Url: null } known && known.MediaId == id;

    /// <summary>One string per preset for a select's value; the source first, since a name is
    /// unique only within its source.</summary>
    internal static string PresetKey(VisualiserPresetSource source, string name) => $"{(int)source}:{name}";

    /// <summary>Whether the look draws bars, and so has a bar count to choose.</summary>
    internal static bool HasBars(IVisualisationLook look)
        => look.PresetSource == VisualiserPresetSource.BuiltIn && look.PresetName is "spectrum-bars" or "mirrored-bars";

    protected override async Task OnInitializedAsync()
    {
        // Looked up rather than injected: a page with no venue to preview against still edits a look.
        if (Services.GetService<IVenuesService>() is { } venues)
            _venuePalette = (await venues.ReadSelectedVenueAsync())?.Settings.ResolveScreenColours().VisualisationPalette;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!ShowPreview) return;

        if (Look is { PresetSource: VisualiserPresetSource.Video, VideoMediaId: { } videoId } && _previewVideo?.MediaId != videoId)
        {
            var video = await Media.ReadAsync(videoId);
            _previewVideo = (videoId, video is not null, video is null ? null : await Backdrops.DirectUrlForAsync(video));
            // The note under the picker follows the answer, and the message below waits for it.
            StateHasChanged();
            return;
        }

        var message = new
        {
            videoUrl = PreviewVideoUrl(),
            presetName = Look.PresetSource == VisualiserPresetSource.Bundled ? Look.PresetName : null,
            presetUrl = Look.PresetSource == VisualiserPresetSource.Imported ? ImportedUrl(Look.PresetName) : null,
            builtIn = Look.PresetSource == VisualiserPresetSource.BuiltIn ? Look.PresetName : null,
            barCount = Look.BarCount,
            colourScheme = Look.ColourScheme.ToString().ToLowerInvariant(),
            colour = Look.Colour,
            brightness = Look.Brightness,
            saturation = Look.Saturation,
            sensitivity = Look.Sensitivity,
            venuePalette = Look.PresetSource == VisualiserPresetSource.BuiltIn && Look.RespectsVenueTheme ? _venuePalette : null,
        };

        var sent = System.Text.Json.JsonSerializer.Serialize(message);
        if (sent == _previewSent) return;
        _previewSent = sent;

        try { await JS.InvokeVoidAsync("khVisualiserPreview.show", _preview, message); }
        catch (JSDisconnectedException) { /* the circuit is going; nothing to preview on */ }
    }

    private async Task SetPresetAsync(ChangeEventArgs e)
    {
        if (ParseKey(e.Value?.ToString()) is not { } preset) return;

        Look.PresetSource = preset.Source;
        Look.PresetName = preset.Name;

        await LookChanged.InvokeAsync(Look);
    }

    private async Task SetVideoAsync(Guid? mediaId)
    {
        if (Look.PresetSource != VisualiserPresetSource.Video) return;

        Look.VideoMediaId = mediaId;
        await LookChanged.InvokeAsync(Look);
    }

    /// <summary>A slider moving: shown in the preview at once, raised when it is let go.</summary>
    private void Adjust(Setting setting, object? value)
    {
        if (int.TryParse(value?.ToString(), out var percent))
            Apply(setting, percent);
    }

    private async Task CommitAsync(Setting setting, object? value)
    {
        if (!int.TryParse(value?.ToString(), out var percent)) return;

        Apply(setting, percent);
        await LookChanged.InvokeAsync(Look);
    }

    private async Task SetBarCountAsync(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var count)) return;

        Look.BarCount = count;
        await LookChanged.InvokeAsync(Look);
    }

    private async Task SetColourSchemeAsync(ChangeEventArgs e)
    {
        if (!Enum.TryParse<VisualiserColourScheme>(e.Value?.ToString(), out var scheme) || !Enum.IsDefined(scheme)) return;

        Look.ColourScheme = scheme;
        await LookChanged.InvokeAsync(Look);
    }

    private async Task SetRespectsVenueThemeAsync(ChangeEventArgs e)
    {
        Look.RespectsVenueTheme = e.Value is true;
        await LookChanged.InvokeAsync(Look);
    }

    private async Task SetColourAsync(string? colour)
    {
        if (colour is null) return;

        Look.Colour = colour;
        await LookChanged.InvokeAsync(Look);
    }

    private void Apply(Setting setting, int percent)
    {
        switch (setting)
        {
            case Setting.Brightness:
                Look.Brightness = Math.Clamp(percent, VisualisationEntry.MinBrightness, VisualisationEntry.MaxBrightness);
                break;
            case Setting.Saturation:
                Look.Saturation = Math.Clamp(percent, VisualisationEntry.MinSaturation, VisualisationEntry.MaxSaturation);
                break;
            case Setting.Sensitivity:
                Look.Sensitivity = Math.Clamp(percent, VisualisationEntry.MinSensitivity, VisualisationEntry.MaxSensitivity);
                break;
        }
    }

    private string? PreviewVideoUrl()
        => Look is { PresetSource: VisualiserPresetSource.Video, VideoMediaId: { } id } && _previewVideo is { } known && known.MediaId == id
            ? known.Url
            : null;

    private static string PresetKey(IVisualisationLook look) => PresetKey(look.PresetSource, look.PresetName);

    private static (VisualiserPresetSource Source, string Name)? ParseKey(string? key)
    {
        var colon = key?.IndexOf(':') ?? -1;
        if (key is null || colon <= 0 || !int.TryParse(key[..colon], out var source) || !Enum.IsDefined((VisualiserPresetSource)source))
            return null;

        return ((VisualiserPresetSource)source, key[(colon + 1)..]);
    }

    /// <summary>What the classic palette is for this look: a calm scene's mix of colours, a retro
    /// effect's own look, or a meter's green to red.</summary>
    private static string ClassicWording(IVisualisationLook look)
        => VisualiserPresetService.IsAmbient(look.PresetSource, look.PresetName) ? "Classic, a soft mix of colours"
           : VisualiserPresetService.IsRetro(look.PresetSource, look.PresetName) ? "Classic, the effect's own colours"
           : "Classic, green to red";

    /// <summary>The imported preset for the preview, versioned so a re-import reloads it.</summary>
    private string? ImportedUrl(string name)
        => Presets.FirstOrDefault(p => p.Source == VisualiserPresetSource.Imported && p.Name == name) is { } preset
            ? $"{VisualiserPresetService.RoutePrefix.TrimStart('/')}{Uri.EscapeDataString(name)}?v={preset.ImportedUtc?.Ticks ?? 0}"
            : null;

    private enum Setting { Brightness, Saturation, Sensitivity }
}
