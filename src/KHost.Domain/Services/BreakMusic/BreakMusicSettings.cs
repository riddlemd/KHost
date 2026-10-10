using KHost.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace KHost.Domain.Services.BreakMusic;

/// <summary>Not folded into <see cref="BreakMusicService"/>: that service is built from every
/// provider, so a provider asking for it in its constructor would close a cycle.</summary>
public sealed class BreakMusicSettings(IOptionsMonitor<BreakMusicService.ServiceOptions> options) : IBreakMusicSettings
{
    public TimeSpan FadeDuration => options.CurrentValue.Fade;
}
