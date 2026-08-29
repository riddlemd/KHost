using System.Globalization;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

public partial class SongControlDial
{
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string? AriaLabel { get; set; }
    [Parameter] public int Value { get; set; }
    [Parameter] public int Min { get; set; }
    [Parameter] public int Max { get; set; }
    [Parameter] public int Step { get; set; } = 1;
    [Parameter] public Func<int, string> Format { get; set; } = v => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Every value the drag passes over, for the readout.</summary>
    [Parameter] public EventCallback<int> ValueInput { get; set; }

    /// <summary>Only the value it was let go of on, for the service.</summary>
    [Parameter] public EventCallback<int> ValueCommitted { get; set; }

    private const double Radius = 26;
    private const double Size = 66;
    private const double Centre = Size / 2;

    private const double Circumference = 2 * Math.PI * Radius;

    /// <summary>Three quarters of the circle, leaving the bottom open the way a knob's throw is.</summary>
    private const double Travel = Circumference * 0.75;

    private SongControlSpan Span => SongControlSpan.For(Value, Min, Max);

    private static string Dashes(double length) => length.ToString("F2", CultureInfo.InvariantCulture);

    private Task OnInputAsync(ChangeEventArgs e) => ValueInput.InvokeAsync(Parse(e));

    private Task OnChangeAsync(ChangeEventArgs e) => ValueCommitted.InvokeAsync(Parse(e));

    // Read off the event, not the parameter: a change can arrive with no input before it, and the
    // parameter then still holds the value the drag started from.
    private int Parse(ChangeEventArgs e) =>
        int.TryParse(e.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : Value;
}
