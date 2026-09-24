namespace KHost.Abstractions.Models;

/// <summary>One unmixed stem, handed to a display that mixes them itself.</summary>
/// <remarks>The host still decides the levels — <see cref="Volume"/> carries the same number
/// <c>AudioMix</c> would have compiled into an ffmpeg filter graph — so a display only applies what
/// it is told and no mixing policy is duplicated where it could drift out of step.
///
/// <para><see cref="Index"/> matches <see cref="AudioTrack.Index"/>, so a stem and the track it came
/// from are the same thing named twice, not two lists to keep aligned.</para></remarks>
public sealed record StemSource(int Index, AudioTrackRole Role, string Url, int Volume);
