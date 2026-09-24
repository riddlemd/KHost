namespace KHost.Abstractions.Services.QueueRotation;

/// <summary>A selectable rotation mode; plugins add modes, resolved by the venue's configured Id.</summary>
/// <remarks>
/// <para>An extension point: a plugin IMPLEMENTS this on a public, concrete class and the host
/// finds it at startup; no registration is needed. The mode then appears in each venue's rotation
/// settings beside the built-in ones, and a venue that picks it stores its <see cref="Id"/>. The
/// Plugins page lists the plugin as providing "Queue rotation".</para>
/// <para>One instance for the life of the host, shared with every other extension interface the same
/// class implements, and possibly called from any thread; keep no per-call state in fields.</para>
/// <para>A venue whose stored id matches no loaded mode, such as one whose plugin was removed,
/// rotates with the built-in <c>fifo</c> mode instead.</para>
/// </remarks>
public interface IQueueRotationMode : IQueueRotationStrategy
{
    /// <summary>Stable machine id (e.g. "fifo"); an id already taken by the host or a plugin wins.</summary>
    /// <remarks>Compared exactly, case included. Built-in modes always win a clash, and a mode that
    /// loses one is ignored without warning, so prefix the id with something of the plugin's own.
    /// Changing it strands every venue that chose the old one. Taken by the host: <c>fifo</c>,
    /// <c>round-robin</c>, <c>reverse</c>, <c>longest-wait-first</c>, <c>fewest-songs-first</c>,
    /// <c>weighted-fair</c>, <c>weighted-lottery</c>, <c>pure-lottery</c>,
    /// <c>shuffle-bucket</c>.</remarks>
    string Id { get; }

    /// <summary>The name a host picks the mode by in venue settings.</summary>
    string Name { get; }

    /// <summary>One or two sentences shown under the picker, saying how the mode orders singers.</summary>
    string Description { get; }
}
