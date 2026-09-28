using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Pages.Settings;

/// <summary>The track/fill/percent trio shared by Downloads and Plugins; each caller keeps its own
/// class names so its scoped CSS reaches this markup through ::deep.</summary>
public partial class ProgressBar
{
    [Parameter, EditorRequired] public double Fraction { get; set; }
    [Parameter, EditorRequired] public string TrackClass { get; set; } = "";
    [Parameter, EditorRequired] public string FillClass { get; set; } = "";
    [Parameter, EditorRequired] public string PercentClass { get; set; } = "";

    private int Percent => (int)Math.Round(Fraction * 100);
}
