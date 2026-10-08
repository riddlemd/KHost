using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace KHost.UserInterface.Components;

/// <summary>Picks one library row of the given types by id, with a Browse button beside it that adds
/// a file from the host's own disk and picks it.</summary>
/// <remarks>The rows are read once, when the picker first renders. Browse is offered only for the
/// types a picked file can be added as (see <see cref="IMediaUploader"/>), and its system picker
/// lists only their extensions.</remarks>
public partial class MediaPicker
{
    [Inject] private IMediaService Media { get; set; } = default!;
    [Inject] private IMediaUploader Uploader { get; set; } = default!;
    [Inject] private IFlashService Flash { get; set; } = default!;

    /// <summary>What the picker offers, and what Browse may add a file as.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<MediaType> Types { get; set; } = [];

    /// <summary>The picked row's id; null for none.</summary>
    [Parameter] public Guid? MediaId { get; set; }

    [Parameter] public EventCallback<Guid?> MediaIdChanged { get; set; }

    /// <summary>Shown while nothing is picked; say what none means here.</summary>
    [Parameter] public string Placeholder { get; set; } = "None";

    /// <summary>False keeps the picker to what is already in the library.</summary>
    [Parameter] public bool AllowBrowse { get; set; } = true;

    /// <summary>The file input's id, for a label or a test to find it by.</summary>
    [Parameter] public string? BrowseId { get; set; }

    [Parameter] public string BrowseTitle { get; set; } = "Browse for a file to add to the library";

    /// <summary>How a row reads in the list; its title unless set.</summary>
    [Parameter] public Func<Media, string>? DisplayName { get; set; }

    [Parameter] public string Class { get; set; } = "";

    private IReadOnlyList<Media> _rows = [];
    private IReadOnlyList<string> _accept = [];
    private Media? _selected;
    private string _text = "";
    private bool _uploading;

    private bool CanBrowse => AllowBrowse && _accept.Count > 0;

    protected override async Task OnInitializedAsync()
    {
        // Read by type rather than paged, or a row past the first page would never be offered.
        _rows = await Media.ReadAllByTypesAsync([.. Types]);
        _accept = Uploader.ExtensionsFor(Types);
        Reselect();
    }

    // The id may be changed from outside after the rows are read, as a Duplicate or Reset would.
    protected override void OnParametersSet() => Reselect();

    private void Reselect()
    {
        if (_selected?.Id == MediaId)
            return;

        _selected = _rows.FirstOrDefault(row => row.Id == MediaId);
        _text = _selected is null ? "" : Describe(_selected);
    }

    private string Describe(Media row) => DisplayName?.Invoke(row) ?? row.Title;

    /// <summary>Clearing the field is how a caller goes back to none.</summary>
    private async Task SelectAsync(Media? row)
    {
        _selected = row;
        await MediaIdChanged.InvokeAsync(row?.Id);
    }

    private Task<IReadOnlyList<Media>> SearchAsync(string term)
        => Task.FromResult<IReadOnlyList<Media>>(
            [.. _rows.Where(row => Describe(row).Contains(term, StringComparison.OrdinalIgnoreCase))]);

    private async Task UploadAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;

        // The system picker's filter is advice a host can switch off, so the name is asked again.
        if (Uploader.TypeFor(file.Name, Types) is not { } type)
        {
            Flash.Show($"{file.Name} cannot be picked here.", FlashType.Warning);
            return;
        }

        // Checked before reading: the stream's own limit surfaces as an IOException, which a full
        // disk raises too, and the two need different words.
        var maxBytes = Uploader.MaxBytesFor(type);
        if (file.Size > maxBytes)
        {
            Flash.Show($"{file.Name} is over {maxBytes / 1024 / 1024} MB, too large to add.", FlashType.Warning);
            return;
        }

        _uploading = true;

        try
        {
            await using var stream = file.OpenReadStream(maxBytes);
            var row = await Uploader.AddAsync(file.Name, stream, type);

            if (_rows.All(existing => existing.Id != row.Id))
                _rows = [.. _rows.Append(row).OrderBy(Describe, StringComparer.OrdinalIgnoreCase)];

            // Set here, not left to the search box: it only fills its text on a pick made in it.
            _text = Describe(row);
            await SelectAsync(row);
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
