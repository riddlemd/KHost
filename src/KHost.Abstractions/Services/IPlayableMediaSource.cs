namespace KHost.Abstractions.Services;

/// <summary>Turns a file the host cannot open into one it can, for the moment it is played.</summary>
/// <remarks>For a provider whose own container the host's encoder does not read. The library row
/// keeps the original — that is the copy worth keeping, because it is the one this provider can
/// always rebuild from — and this hands over something playable only while a song is on.
///
/// <para><b>A resolver is a cheap transform and nothing else</b>: a remux, a stream copy, an
/// extraction. It keeps no cache, reports no progress and holds nothing past the song. If one costs
/// more than about a second, the work belongs at import instead, since every song start waits for
/// it.</para>
///
/// <para>The result is scratch. It is written into the working directory the host supplies and deleted
/// with it when the stream session closes, so a resolver keeps no files of its own and cleans up
/// nothing.</para>
///
/// <para>An extension point: a plugin IMPLEMENTS it and the host discovers it. Asked whenever the
/// host opens an encode, the first source to claim a file and return a path wins. The plugin's
/// object is one singleton shared across every extension interface it implements, and is called
/// from any thread.</para></remarks>
public interface IPlayableMediaSource
{
    /// <summary>Whether this source has something to do with that file. Answered from the path
    /// alone, like <see cref="IMediaProbe.CanProbe"/>: it is asked before every stream opens,
    /// including for files nothing owns.</summary>
    /// <remarks>A throw here is logged and the source skipped for that file.</remarks>
    bool CanResolve(string filePath);

    /// <summary>The path the host should open instead, or null to open the original.</summary>
    /// <param name="filePath">The library file about to be streamed; one this source claimed.</param>
    /// <param name="workingDirectory">Somewhere to write, already created. It is the stream
    /// session's own directory and is removed when the session closes.</param>
    /// <param name="cancellationToken">Fires when the song start is abandoned; stop and throw.</param>
    /// <remarks>Null (or empty) is not a failure — it means there was nothing to do, and the next
    /// source that claims the file is asked. A resolver that cannot convert a file it claimed should
    /// throw: the exception is not caught, so the song fails with that cause rather than handing the
    /// encoder a container it will reject as "Invalid data found".</remarks>
    Task<string?> ResolvePlayableAsync(
        string filePath,
        string workingDirectory,
        CancellationToken cancellationToken = default);

}
