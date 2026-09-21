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
    /// <remarks>Playback resolves it at load, so a venue edited mid-song does not rename whoever is
    /// at the microphone. Never feed it back into the add-a-singer lookup: that path creates a user
    /// on no match, and a one-off name would mint a phantom singer. Null, not "", means their own
    /// name.</remarks>
    public string? SungAs { get; set; }

    public DateTime CreatedDate { get; set; }
}
