using System.Reflection;
using KHost.Abstractions.Models;
using KHost.IPC.SignalR.Contracts;
using KHost.IPC.SignalR;

namespace KHost.UnitTests.IPC;

/// <summary>A screen off the loopback address is sent a rewritten load, and everything else about
/// that load has to survive the rewrite.</summary>
public class LoadMediaCommandRewriteTests
{
    private static LoadMediaCommand Populated() => new()
    {
        StreamUrl = "http://127.0.0.1:5251/media/s1/stream.m3u8",
        StreamStartOffset = TimeSpan.FromSeconds(42),
        Tempo = -30,
        Stems = [new StemSource(0, AudioTrackRole.Music, "http://127.0.0.1:5251/media/s1/stem0.ogg", 100)],
    };

    [Fact]
    public void WithStreamUrl_ChangesTheUrlAndNothingElse()
    {
        var original = Populated();

        var rewritten = ScreenServerService.WithStreamUrl(original, "http://192.168.1.10:5251/media/s1/stream.m3u8");

        Assert.Equal("http://192.168.1.10:5251/media/s1/stream.m3u8", rewritten.StreamUrl);
        Assert.Equal(original.StreamStartOffset, rewritten.StreamStartOffset);
        Assert.Equal(original.Tempo, rewritten.Tempo);
        Assert.Equal(original.Stems, rewritten.Stems);
    }

    /// <summary>The failure this exists for: the rewrite rebuilds the command by hand, so a property
    /// added to it and not added here goes missing only for screens on another address — which is
    /// how Tempo was lost, and the symptom was words drifting on one screen and not another.</summary>
    [Fact]
    public void WithStreamUrl_CarriesEveryPropertyTheCommandHas()
    {
        var original = Populated();
        var rewritten = ScreenServerService.WithStreamUrl(original, "http://192.168.1.10:5251/x.m3u8");

        var dropped = typeof(LoadMediaCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != nameof(LoadMediaCommand.StreamUrl))
            .Where(property => !Equals(property.GetValue(original), property.GetValue(rewritten)))
            .Select(property => property.Name)
            .ToList();

        Assert.True(
            dropped.Count == 0,
            $"Not carried through the rewrite: {string.Join(", ", dropped)}. Add them to WithStreamUrl.");
    }
}
