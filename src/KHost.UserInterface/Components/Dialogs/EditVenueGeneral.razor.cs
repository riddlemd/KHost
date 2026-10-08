using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "General" section: the placeholder image and what the "Up next"
/// card is drawn over.</summary>
public partial class EditVenueGeneral
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    [Inject] private IMediaService Media { get; set; } = default!;

    private IReadOnlyList<Media> _images = [];
    private Media? _brandingImage;
    private string _brandingImageText = "";

    /// <summary>Read when the dialog opens, not held, since a newly scanned image would be missing.</summary>
    protected override async Task OnInitializedAsync()
    {
        // Stills only: anything else handed to the screen as a card is a URL that serves nothing.
        // Read by type rather than paged, or a card past the first page would never be offered.
        _images = await Media.ReadAllByTypesAsync(MediaType.Image);

        _brandingImage = _images.FirstOrDefault(image => image.Id == Model.BrandingImageMediaId);
        _brandingImageText = _brandingImage?.Title ?? "";
    }

    /// <summary>Clearing the field is how a venue goes back to showing no card at all.</summary>
    private void OnBrandingImageChanged(Media? image)
    {
        _brandingImage = image;
        Model.BrandingImageMediaId = image?.Id;
    }

    private Task<IReadOnlyList<Media>> SearchImagesAsync(string term)
        => Task.FromResult<IReadOnlyList<Media>>(
            [.. _images.Where(image => image.Title.Contains(term, StringComparison.OrdinalIgnoreCase))]);
}
