using KHost.Abstractions.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>A select's options for every preset, built-in then shipped then imported, valued by
/// <see cref="VisualisationsManagerPage.PresetKey"/>.</summary>
public partial class VisualiserPresetOptions
{
    [Parameter, EditorRequired] public IReadOnlyList<VisualiserPreset> Presets { get; set; } = [];
}
