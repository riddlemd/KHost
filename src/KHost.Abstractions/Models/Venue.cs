using KHost.Plugins.Sdk.Models.QueueRotation;

namespace KHost.Abstractions.Models;

public class Venue : RepositoryModel
{
    public bool Enabled { get; set; } = true;
    public required string Name { get; set; }

    /// <summary>The name as search matches it. Written by the persistence layer, not by hand.</summary>
    public string NameFolded { get; set; } = string.Empty;
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
        public bool ShowEstimatedWaitTime { get; set; } = true;
        public bool TippingEnabled { get; set; } = true;
        // Off by default — it adds a prompt, so venues opt in rather than inherit one.
        public bool WarnOnDuplicateSong { get; set; }
        public int DuplicateSongWindowHours { get; set; } = 4;
        public bool PromptBeforeRemovingSinger { get; set; } = true;
        public bool PromptBeforeRemovingPerformance { get; set; } = true;
        public bool ClearQueueOnClose { get; set; } = true;

        // Nullable: EF reads venue rows saved before this key existed as null (initializers
        // are ignored for missing JSON keys) — callers fall back to a default config.
        public QueueRotationConfig? QueueRotation { get; set; }

        /// <summary>Which pool break music draws from. Null means the venue has not chosen one.</summary>
        public Guid? BreakMusicPoolId { get; set; }

        /// <summary><see cref="IBreakMusicProvider.SourceName"/>; null falls back to the built-in one.</summary>
        public string? BreakMusicProvider { get; set; }

        // Nullable for the same reason as QueueRotation: a stored row without this key reads as
        // default, and an int would arrive as 0 — break music that plays silently.
        public int? BreakMusicVolume { get; set; }

        /// <summary>Memberwise copy plus a deep copy of the one reference-type member.</summary>
        public VenueSettings Clone()
        {
            var clone = (VenueSettings)MemberwiseClone();

            clone.QueueRotation = QueueRotation?.Clone();

            return clone;
        }
    }
}
