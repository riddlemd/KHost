using System.Text.Json.Serialization;

namespace KHost.Abstractions.Services.IPC;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(LoadMediaCommand), "loadMedia")]
[JsonDerivedType(typeof(PlayCommand), "play")]
[JsonDerivedType(typeof(PauseCommand), "pause")]
[JsonDerivedType(typeof(StopCommand), "stop")]
[JsonDerivedType(typeof(SeekCommand), "seek")]
[JsonDerivedType(typeof(SetVolumeCommand), "setVolume")]
[JsonDerivedType(typeof(SetPitchCommand), "setPitch")]
public abstract class ScreenCommandBase : IScreenCommand { }

public sealed class LoadMediaCommand : ScreenCommandBase
{
    public required string FilePath { get; init; }
}

public sealed class PlayCommand : ScreenCommandBase { }
public sealed class PauseCommand : ScreenCommandBase { }

public sealed class StopCommand : ScreenCommandBase
{
    public TimeSpan? FadeDuration { get; init; }
}

public sealed class SeekCommand : ScreenCommandBase
{
    public required TimeSpan Position { get; init; }
}

public sealed class SetVolumeCommand : ScreenCommandBase
{
    public required float Volume { get; init; }
}

public sealed class SetPitchCommand : ScreenCommandBase
{
    public required int Semitones { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ScreenPlaybackState), "playback")]
public abstract class ScreenStateBase : IScreenState { }

public sealed class ScreenPlaybackState : ScreenStateBase
{
    public required string? LoadedFilePath { get; init; }
    public required bool IsPlaying { get; init; }
    public required TimeSpan Position { get; init; }
    public required TimeSpan Duration { get; init; }
}
