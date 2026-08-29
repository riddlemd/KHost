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

    public DateTime CreatedDate { get; set; }
}
