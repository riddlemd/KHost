namespace KHost.Abstractions.Models;

/// <summary>What a track carries; multi-track karaoke ships backing music apart from the voices.</summary>
public enum AudioTrackRole
{
    /// <summary>The backing track. Always full level; the others are set against it.</summary>
    Music,

    /// <summary>The original lead vocal, which the singer is there to replace.</summary>
    Lead,

    /// <summary>Harmony and backing voices, which a singer usually wants left in.</summary>
    Backing,
}

/// <summary>One audio stream; <see cref="Index"/> is its stream position, ffmpeg's <c>0:a:N</c>.</summary>
public sealed record AudioTrack(int Index, AudioTrackRole Role, string Name);
