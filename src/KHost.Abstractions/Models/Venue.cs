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

    /// <summary>Copies under a fresh id; Settings is deep-copied, the rest memberwise.</summary>
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
        // Off by default: it adds a prompt, so venues opt in rather than inherit one.
        public bool WarnOnDuplicateSong { get; set; }
        public int DuplicateSongWindowHours { get; set; } = 4;
        public bool PromptBeforeRemovingSinger { get; set; } = true;
        public bool PromptBeforeRemovingPerformance { get; set; } = true;
        public bool ClearQueueOnClose { get; set; } = true;

        // Nullable: EF reads venue rows saved before this key existed as null (initializers
        // are ignored for missing JSON keys); callers fall back to a default config.
        public QueueRotationConfig? QueueRotation { get; set; }

        /// <summary>Shown on screen whenever nothing is playing. Null leaves the screen blank.</summary>
        public Guid? BrandingImageMediaId { get; set; }

        /// <summary>The one ad playlist that may fire. Null means this venue runs no ads.</summary>
        public Guid? AdPoolId { get; set; }

        /// <summary>Which pool break music draws from. Null means the venue has not chosen one.</summary>
        public Guid? BreakMusicPoolId { get; set; }

        /// <summary>Provider source name; null falls back to the built-in one.</summary>
        public string? BreakMusicProvider { get; set; }

        /// <summary>Whether a queued alias is shown. Off when unset, so it needs no backfill.</summary>
        public bool AllowAliases { get; set; }

        // Every marquee setting reads "off" when its key is missing, so it needs no backfill.

        /// <summary>Whether the screen carries a marquee at all.</summary>
        public bool MarqueeEnabled { get; set; }

        /// <summary>How many singers ahead the room is shown. Zero is a message-only marquee.</summary>
        public int MarqueeSingerCount { get; set; }

        /// <summary>The venue's own line, e.g. a drink special. Null shows only singers.</summary>
        public string? MarqueeMessage { get; set; }

        /// <summary>Up-next line; tags like <c>{song}</c> replace per singer. Blank uses a default.</summary>
        public string? MarqueeEntryFormat { get; set; }

        public MarqueePosition MarqueePosition { get; set; }

        /// <summary>Null takes the screen's own default, which is what most venues want.</summary>
        public string? MarqueeBackgroundColor { get; set; }

        public string? MarqueeTextColor { get; set; }

        /// <summary>Text height in pixels; zero takes the screen's own size, em-sizing the rest.</summary>
        public int MarqueeFontSizePixels { get; set; }

        /// <summary>Pixels a second; zero takes the screen's own speed, not a lap time.</summary>
        public int MarqueeScrollSpeed { get; set; }

        /// <summary>Pins "Up next" at the leading edge, unscrolled; a modifier, not a style.</summary>
        public bool MarqueePinLabel { get; set; }

        /// <summary>Which plugin's QR code shows; null (default) means none shown until chosen.</summary>
        public string? QrCodeSource { get; set; }

        /// <summary>Corner the code sits in; null reads as "no preference", not bottom-right.</summary>
        public ScreenCorner? QrCodeCorner { get; set; }

        /// <summary>How big it is drawn. Null takes medium.</summary>
        public ScreenQrSize? QrCodeSize { get; set; }

        /// <summary>Hides the code while someone sings; off when unset, so vanishing is default.</summary>
        public bool QrCodeHideDuringSong { get; set; }

        /// <summary>Quiet zone in modules (what a scanner measures); zero takes the screen's own.</summary>
        public int QrCodeSafeZone { get; set; }

        /// <summary>Inset from the edges, as a percent of the shorter side, so it scales by screen.</summary>
        public double QrCodeOffset { get; set; }

        /// <summary>How the placeholder image fills the screen, or null for the image's own answer.</summary>
        /// <remarks>An override only: the media row's own scaling still answers otherwise.</remarks>
        public ImageScaling? BrandingImageScaling { get; set; }

        /// <summary>Whether the screen names what's playing between singers. Off when unset.</summary>
        public bool BreakMusicCardEnabled { get; set; }

        /// <summary>Which corner names it; null takes bottom-left, not the QR's bottom-right.</summary>
        public ScreenCorner? BreakMusicCardCorner { get; set; }

        /// <summary>Memberwise copy plus a deep copy of the one reference-type member.</summary>
        public VenueSettings Clone()
        {
            var clone = (VenueSettings)MemberwiseClone();

            clone.QueueRotation = QueueRotation?.Clone();

            return clone;
        }
    }
}
