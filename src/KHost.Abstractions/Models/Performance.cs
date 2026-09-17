namespace KHost.Abstractions.Models;

public class Performance : RepositoryModel
{
    public Guid SingerId { get; set; }
    public Guid MediaId { get; set; }
    public Guid? VenueId { get; set; }
    public int? QueuePosition { get; set; }

    /// <summary>
    /// Semitones. Survives the song: a dequeue only nulls <see cref="QueuePosition"/>, so the
    /// history carries the key it was sung in.
    /// </summary>
    public int Pitch { get; set; }

    /// <summary>
    /// Percent either side of the recorded speed. Survives the song for the same reason as
    /// <see cref="Pitch"/>, and comes back with it when the row is re-queued.
    /// </summary>
    public int Tempo { get; set; }

    /// <summary>
    /// How loud the original lead vocal rode, for a file that ships its voices apart. Zero by
    /// default: the singer is there to replace it.
    /// </summary>
    public int LeadVolume { get; set; }

    /// <summary>
    /// How loud the backing voices rode. Null means the host never touched it, so the machine
    /// setting answers — and keeps answering if that setting later changes.
    /// </summary>
    public int? BackingVolume { get; set; }

    /// <summary>
    /// The name this was sung under, recorded when it was queued rather than looked up when it is
    /// shown. A performance keeps no foreign key — deleting a singer leaves every sung row
    /// standing — so without this a history row outlives the only thing that could name it.
    ///
    /// Written on every enqueue, so it is normally just the singer's name at the time. It differs
    /// when a caller had a name of its own to record: the provider's queue is song-first and a guest
    /// types a nickname per pick, which is the same person under another name rather than another
    /// person. Null only on rows queued before this existed whose singer is already gone.
    /// </summary>
    public string? SungAs { get; set; }

    public DateTime CreatedDate { get; set; }
}
