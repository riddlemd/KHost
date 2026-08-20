namespace KHost.Abstractions.Models;

public class Tip : RepositoryModel
{
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public Guid UserId { get; set; }
    public Guid? VenueId { get; set; }
    public decimal Amount { get; set; }
    public TipPaymentMethod PaymentMethod { get; set; }
    private string _notes = string.Empty;

    /// <summary>Setting this refolds <see cref="NotesFolded"/> with it, so the two cannot drift apart.</summary>
    public string Notes
    {
        get => _notes;
        set
        {
            _notes = value;
            NotesFolded = TextFolding.Fold(value);
        }
    }

    /// <summary>The notes as search matches them: composed and lowercased.</summary>
    public string NotesFolded { get; private set; } = string.Empty;
}
