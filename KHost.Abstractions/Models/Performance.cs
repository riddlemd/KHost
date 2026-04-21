namespace KHost.Abstractions.Models;

public class Performance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SingerId { get; set; }
    public Guid MediaId { get; set; }
    public int? QueuePosition { get; set; }
    public DateTime CreatedDate { get; set; }
}
