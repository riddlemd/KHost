using System.Globalization;
using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components;

/// <summary>Picks a placeholder image and how it fills the screen, with a preview drawn the way the
/// screen will draw it.</summary>
public partial class PlaceholderImageEditor
{
    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The picked image; null for none.</summary>
    [Parameter] public Guid? MediaId { get; set; }

    [Parameter] public EventCallback<Guid?> MediaIdChanged { get; set; }

    /// <summary>How it fills the screen; null for the image's own.</summary>
    [Parameter] public ImageScaling? Scaling { get; set; }

    [Parameter] public EventCallback<ImageScaling?> ScalingChanged { get; set; }

    /// <summary>False where the caller already names the picker in its own label.</summary>
    [Parameter] public bool ShowPickerLabel { get; set; } = true;

    /// <summary>Prefixes the browse input's and the scaling select's ids, so two on a page stay apart.</summary>
    [Parameter, EditorRequired] public string IdPrefix { get; set; } = "";

    /// <summary>The preview stands for a 1920-wide screen, so Original draws the picture at its share of that width.</summary>
    internal const int PreviewScreenWidth = 1920;

    private Media? _media;
    private Guid? _readFor;
    private ElementReference _image;
    private int _naturalWidth;

    /// <summary>What the preview draws: the choice, else the picture's own, else Fit as the library defaults.</summary>
    private ImageScaling ShownScaling => Scaling ?? _media?.ImageScaling ?? ImageScaling.Fit;

    // Only Original needs the picture's own width; until it is known the picture shows at its own pixels.
    private string? OriginalWidthStyle => ShownScaling == ImageScaling.Original && _naturalWidth > 0
        ? string.Create(CultureInfo.InvariantCulture, $"width: {_naturalWidth * 100.0 / PreviewScreenWidth:0.##}%")
        : null;

    // Read whenever the id changes from outside too (a caller resetting it), not only on a pick here.
    protected override async Task OnParametersSetAsync()
    {
        if (_readFor == MediaId && (_media is not null || MediaId is null)) return;

        _readFor = MediaId;
        _naturalWidth = 0;
        _media = MediaId is { } id ? await Media.ReadAsync(id) : null;
    }

    private async Task OnImageChangedAsync(Guid? mediaId)
    {
        MediaId = mediaId;
        await OnParametersSetAsync();
        await MediaIdChanged.InvokeAsync(mediaId);
    }

    private async Task OnScalingChangedAsync(ChangeEventArgs e)
    {
        Scaling = Enum.TryParse<ImageScaling>(e.Value?.ToString(), out var scaling) ? scaling : null;
        await ScalingChanged.InvokeAsync(Scaling);
    }

    // The library keeps no pixel size, so it is read off the picture the browser loaded.
    private async Task OnImageLoadedAsync()
    {
        try { _naturalWidth = await JS.InvokeAsync<int>("Reflect.get", _image, "naturalWidth"); }
        catch (JSDisconnectedException) { /* the circuit is going; nothing to draw on */ }
    }

    private static string ScalingLabel(ImageScaling scaling) => scaling switch
    {
        ImageScaling.Fit => "Fit: show all of it",
        ImageScaling.Fill => "Fill: crop the overflow",
        ImageScaling.Stretch => "Stretch: distort to fit",
        _ => "Original size",
    };
}
