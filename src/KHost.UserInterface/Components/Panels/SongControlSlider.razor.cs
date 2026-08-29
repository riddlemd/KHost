using System.Globalization;
using KHost.UserInterface.Models;
using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components.Panels;

public partial class SongControlSlider
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

    private SongControlSpan Span => SongControlSpan.For(Value, Min, Max);

    /// <summary>
    /// The track is painted from custom properties rather than a stylesheet because the span is a
    /// value, not a state. Hard stops, so the colour does not bleed past where it ends.
    /// </summary>
    private string FillStyle => FormattableString.Invariant(
        $"--from-frac:{Span.From:F4};--to-frac:{Span.To:F4};--fill:{Span.Colour}");

    private Task OnInputAsync(ChangeEventArgs e) => ValueInput.InvokeAsync(Parse(e));

    private Task OnChangeAsync(ChangeEventArgs e) => ValueCommitted.InvokeAsync(Parse(e));

    // Read off the event, not the parameter: a change can arrive with no input before it, and the
    // parameter then still holds the value the drag started from.
    private int Parse(ChangeEventArgs e) =>
        int.TryParse(e.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : Value;
}
