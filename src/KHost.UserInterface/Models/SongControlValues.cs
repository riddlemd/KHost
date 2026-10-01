namespace KHost.UserInterface.Models;

/// <summary>Which of the song controls moved.</summary>
public enum SongControlSetting
{
    Pitch,
    Tempo,
    Lead,
    Backing,
    Voice,
}

/// <summary>One control moved to <paramref name="Value"/>; <paramref name="Voice"/> names the
/// singer for <see cref="SongControlSetting.Voice"/>.</summary>
public readonly record struct SongControlChange(SongControlSetting Setting, int Value, string? Voice = null);

/// <summary>What the song controls show. Held by whoever owns the values — the playing song or a
/// turn being edited — and written by the controls as they move.</summary>
public sealed class SongControlValues
{
    public int Pitch { get; set; }
    public int Tempo { get; set; }
    public int Lead { get; set; }
    public int Backing { get; set; }
    public Dictionary<string, int> Voices { get; set; } = [];

    public void Apply(SongControlChange change)
    {
        switch (change.Setting)
        {
            case SongControlSetting.Pitch: Pitch = change.Value; break;
            case SongControlSetting.Tempo: Tempo = change.Value; break;
            case SongControlSetting.Lead: Lead = change.Value; break;
            case SongControlSetting.Backing: Backing = change.Value; break;
            case SongControlSetting.Voice when change.Voice is not null: Voices[change.Voice] = change.Value; break;
        }
    }
}
