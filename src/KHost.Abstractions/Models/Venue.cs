namespace KHost.Abstractions.Models;

public class Venue : RepositoryModel
{
    public bool Enabled { get; set; } = true;
    public required string Name { get; set; }
    public string Notes { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public VenueSettings Settings { get; set; } = new();

    /// <summary>
    /// Copy under a fresh id. Memberwise so new properties are carried without a code change;
    /// Settings is the only reference type, so it gets copied rather than shared.
    /// </summary>
    public Venue CloneAs(string name)
    {
        var clone = (Venue)MemberwiseClone();

        clone.Id = Guid.NewGuid();
        clone.Name = name;
        clone.Settings = Settings.Clone();

        return clone;
    }

    public class VenueSettings
    {
        public int DefaultVolume { get; set; } = 100;
        public ScreenDisconnectBehavior OnScreenDisconnect { get; set; } = ScreenDisconnectBehavior.ResumeOnReconnect;
        public bool MoveSingerToBottomAfterPerformance { get; set; } = true;
        public bool ShowEstimatedWaitTime { get; set; } = true;
        public bool TippingEnabled { get; set; } = true;
        public bool PromptBeforeRemovingSinger { get; set; } = true;
        public bool PromptBeforeRemovingPerformance { get; set; } = true;
        public bool ClearQueueOnClose { get; set; } = true;

        /// <summary>Every member is a value type, so a memberwise copy is a full copy.</summary>
        public VenueSettings Clone() => (VenueSettings)MemberwiseClone();
    }
}
