using KHost.Abstractions.Models.QueueRotation;

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

        /// <summary>Shown on screen whenever nothing is playing. Null leaves the screen blank.</summary>
        public Guid? BrandingImageMediaId { get; set; }

        /// <summary>The one ad playlist that may fire. Null means this venue runs no ads.</summary>
        public Guid? AdPoolId { get; set; }

        /// <summary>Which pool break music draws from. Null means the venue has not chosen one.</summary>
        public Guid? BreakMusicPoolId { get; set; }

        /// <summary><see cref="IBreakMusicProvider.SourceName"/>; null falls back to the built-in one.</summary>
        public string? BreakMusicProvider { get; set; }

        // Every marquee setting reads as "off" when its key is missing, so a venue saved before
        // the feature existed needs no backfill migration: EF ignores property initializers for
        // absent JSON keys, and false/0/null are exactly the right answers for a venue that has
        // never been asked. The dialog supplies the sensible starting values instead.

        /// <summary>Whether the screen carries a marquee at all.</summary>
        public bool MarqueeEnabled { get; set; }

        /// <summary>How many singers ahead the room is shown. Zero is a message-only marquee.</summary>
        public int MarqueeSingerCount { get; set; }

        /// <summary>The venue's own line — a drink special, a closing time. Null shows only singers.</summary>
        public string? MarqueeMessage { get; set; }

        public MarqueePosition MarqueePosition { get; set; }

        /// <summary>Null takes the screen's own default, which is what most venues want.</summary>
        public string? MarqueeBackgroundColor { get; set; }

        public string? MarqueeTextColor { get; set; }

        /// <summary>
        /// Height of the text in pixels. Zero takes the screen's own size — which is also what a
        /// venue saved before this key existed reads as. Everything else in the band is sized in
        /// em, so this scales the whole thing rather than only the letters.
        /// </summary>
        public int MarqueeFontSizePixels { get; set; }

        /// <summary>
        /// How fast the band travels, in pixels a second. Zero takes the screen's own speed. A
        /// rate rather than a lap time, so a long line does not race to keep a short one's pace.
        /// </summary>
        public int MarqueeScrollSpeed { get; set; }

        /// <summary>
        /// Anchors the "Up next" label at the leading edge, outside the scroll, so a room glancing
        /// up always sees what the list is. A modifier on whatever the band otherwise looks like,
        /// not a look of its own.
        /// </summary>
        public bool MarqueePinLabel { get; set; }

        /// <summary>Memberwise copy plus a deep copy of the one reference-type member.</summary>
        public VenueSettings Clone()
        {
            var clone = (VenueSettings)MemberwiseClone();

            clone.QueueRotation = QueueRotation?.Clone();

            return clone;
        }
    }
}
