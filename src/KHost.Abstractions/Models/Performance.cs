namespace KHost.Abstractions.Models;

public class Performance : RepositoryModel
{
    public Guid SingerId { get; set; }
    public Guid MediaId { get; set; }
    public Guid? VenueId { get; set; }
    public int? QueuePosition { get; set; }

    /// <summary>Semitones; survives the song, as a dequeue only nulls <see cref="QueuePosition"/>.</summary>
    public int Pitch { get; set; }

    /// <summary>Percent either side of recorded speed; survives the song like <see cref="Pitch"/>.</summary>
    public int Tempo { get; set; }

    /// <summary>Lead vocal volume, for a file shipping voices apart. Zero by default.</summary>
    public int LeadVolume { get; set; }

    /// <summary>Backing volume; null means untouched, so the machine setting answers.</summary>
    public int? BackingVolume { get; set; }

    /// <summary>Name sung under, recorded at enqueue since a performance keeps no foreign key.</summary>
    public string? SungAs { get; set; }

    public DateTime CreatedDate { get; set; }
}
