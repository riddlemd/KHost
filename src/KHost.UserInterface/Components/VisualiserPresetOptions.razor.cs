using KHost.Abstractions.Models;
using KHost.Domain.Services.Visualisations;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

/// <summary>A select's options for every preset, the built-in analysers then the ambient scenes then
/// the retro effects then shipped then imported, valued by <see cref="VisualisationLookEditor.PresetKey"/>.</summary>
public partial class VisualiserPresetOptions
{
    [Parameter, EditorRequired] public IReadOnlyList<VisualiserPreset> Presets { get; set; } = [];

    private IEnumerable<VisualiserPreset> Analysers
        => Presets.Where(p => p.Source == VisualiserPresetSource.BuiltIn
            && !VisualiserPresetService.IsAmbient(p.Source, p.Name) && !VisualiserPresetService.IsRetro(p.Source, p.Name));

    private IEnumerable<VisualiserPreset> Ambient
        => Presets.Where(p => VisualiserPresetService.IsAmbient(p.Source, p.Name));

    private IEnumerable<VisualiserPreset> Retro
        => Presets.Where(p => VisualiserPresetService.IsRetro(p.Source, p.Name));
}
