using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Common.Media;
using KHost.Domain.Services;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components.Dialogs;

/// <summary>The venue dialog's "General" section: the placeholder image and what the "Up next"
/// card is drawn over.</summary>
public partial class EditVenueGeneral
{
    [Parameter, EditorRequired] public EditVenueModel Model { get; set; } = default!;

    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IImageUploader Uploader { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    private IReadOnlyList<Media> _images = [];
    private Media? _brandingImage;
    private string _brandingImageText = "";
    private bool _uploading;

    /// <summary>The picker's filter: every still the screen can show, and nothing else.</summary>
    private static string ImageAccept => string.Join(",", MediaFormats.ImageExtensions);

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

    private async Task UploadAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;

        // Checked before reading: the stream's own limit surfaces as an IOException, which a full
        // disk raises too, and the two need different words.
        if (file.Size > Uploader.MaxBytes)
        {
            Flash.Show($"{file.Name} is over {Uploader.MaxBytes / 1024 / 1024} MB, too large for a placeholder.", FlashType.Warning);
            return;
        }

        _uploading = true;

        try
        {
            await using var stream = file.OpenReadStream(Uploader.MaxBytes);
            var image = await Uploader.AddAsync(file.Name, stream);

            if (_images.All(existing => existing.Id != image.Id))
                _images = [.. _images.Append(image).OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)];

            OnBrandingImageChanged(image);
        }
        catch (Exception ex)
        {
            Flash.Show($"Could not add {file.Name}: {ex.Message}", FlashType.Warning);
        }
        finally
        {
            _uploading = false;
        }
    }
}
