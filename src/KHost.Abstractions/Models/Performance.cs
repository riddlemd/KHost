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

    public DateTime CreatedDate { get; set; }
}
