using KHost.Abstractions.Models;

namespace KHost.Abstractions.Services;

/// <summary>Who sings next, in order, each with the song they will sing.</summary>
/// <remarks>Host-owned; a plugin has nothing to implement. A display provider that names the next
/// singers takes it, reads <see cref="ReadAsync"/> on connect, and reads again whenever it hears
/// <see cref="KHost.Abstractions.Messaging.Messages.UpNextChanged"/>, which is the one message that
/// covers every way the list can move. A host singleton, callable from any thread.</remarks>
public interface IUpNextService
{
    /// <summary>The next <paramref name="count"/> singers in queue order, fewer when fewer are
    /// waiting, and empty for a count of zero or less.</summary>
    /// <remarks>The singer at the microphone is left out before counting, so asking for three names
    /// three people who have yet to sing. Each singer appears once, with the first song they have
    /// queued; a singer with nothing queued is still listed, with no song. The whole answer each
    /// time.</remarks>
    Task<IReadOnlyList<UpNextEntry>> ReadAsync(int count, CancellationToken cancellationToken = default);
}
