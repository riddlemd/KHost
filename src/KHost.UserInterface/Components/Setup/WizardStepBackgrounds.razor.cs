using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.DataAccess.Services;
using KHost.UserInterface.Models;
using KHost.UserInterface.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Setup;

/// <summary>Picks the shipped visualisation playlist new venues start on, before the first venue is
/// made, so that venue starts on it too.</summary>
public partial class WizardStepBackgrounds
{
    [Inject] private IAppSettingsService AppSettings { get; set; } = default!;
    [Inject] private IVisualiserPresetService Presets { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public EventCallback OnComplete { get; set; }

    private VenueBackgrounds _choice;
    private bool _isSaving;
    private bool _previewsSent;
    private string _serverError = string.Empty;

    internal IReadOnlyList<BackgroundsOption> Options { get; private set; } = [];

    protected override void OnInitialized()
    {
        _choice = AppSettings.Current.NewVenueBackgrounds;

        var ambient = Presets.ReadAll()
            .Where(p => p.Source == VisualiserPresetSource.BuiltIn && p.Name.StartsWith("ambient-", StringComparison.Ordinal))
            .ToList();
        string[] TitlesOf(bool advanced) =>
            [.. ambient.Where(p => ShippedVisualisationPlaylists.AdvancedScenes.Contains(p.Name) == advanced).Select(p => p.Title ?? p.Name)];

        Options =
        [
            new(VenueBackgrounds.Basic, "Basic Backgrounds", "ambient-bokeh",
                "Simple shapes and designs, good for low end machines.", TitlesOf(false)),
            new(VenueBackgrounds.Advanced, "Advanced Backgrounds", "ambient-nebula",
                "Complex shader based designs, good for performant machines.", TitlesOf(true)),
        ];
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_previewsSent) return;
        _previewsSent = true;

        foreach (var option in Options)
        {
            var message = new
            {
                builtIn = option.PreviewScene,
                barCount = 32,
                colourScheme = "classic",
                colour = VisualisationEntry.DefaultColour,
                brightness = 100,
                saturation = 100,
                sensitivity = 100,
            };

            try { await JS.InvokeVoidAsync("khVisualiserPreview.show", option.Frame, message); }
            catch (JSDisconnectedException) { return; }
        }
    }

    private async Task OnNextAsync()
    {
        _serverError = string.Empty;
        _isSaving = true;

        try
        {
            await AppSettings.SaveNewVenueBackgroundsAsync(_choice);
            await OnComplete.InvokeAsync();
        }
        catch (IOException ex)
        {
            _serverError = $"Could not save the choice: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    internal sealed class BackgroundsOption(
        VenueBackgrounds backgrounds, string title, string previewScene, string description, IReadOnlyList<string> scenes)
    {
        public VenueBackgrounds Backgrounds { get; } = backgrounds;
        public string Title { get; } = title;
        public string PreviewScene { get; } = previewScene;
        public string Description { get; } = description;
        public IReadOnlyList<string> Scenes { get; } = scenes;
        public ElementReference Frame { get; set; }
    }
}
