namespace KHost.UserInterface.Models
{
    public class Venue
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public bool Enabled { get; set; } = true;
        public required string Name { get; set; }
        public string Notes { get; set; } = "";
        public DateTimeOffset LastUpdated { get; set; }
    }
}
