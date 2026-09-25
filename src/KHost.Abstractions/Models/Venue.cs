using KHost.Abstractions.Models.QueueRotation;

namespace KHost.Abstractions.Models;

/// <summary>One room the host runs a show in, with its own settings.</summary>
public class Venue : RepositoryModel
{
    /// <summary>Whether this venue is offered for selection. A disabled venue's data is kept, not
    /// deleted.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The venue's name, shown throughout the host.</summary>
    public required string Name { get; set; }

    /// <summary>A search-friendly form of <see cref="Name"/>, computed by the host — do not set
    /// this directly.</summary>
    public string NameFolded { get; set; } = string.Empty;

    /// <summary>Free-form notes about the venue.</summary>
    public string Notes { get; set; } = "";

    /// <summary>The venue's street address.</summary>
    public string Address { get; set; } = "";

    /// <summary>The venue's phone number.</summary>
    public string Phone { get; set; } = "";

    /// <summary>This venue's own settings.</summary>
    public VenueSettings Settings { get; set; } = new();

    /// <summary>Copies this venue under a fresh id and a new name, as an independent venue with its
    /// own settings.</summary>
    public Venue CloneAs(string name)
    {
        var clone = (Venue)MemberwiseClone();

        clone.Id = Guid.NewGuid();
        clone.Name = name;
        clone.Settings = Settings.Clone();

        return clone;
    }

    /// <summary>A venue's own settings: the show's rules, the screen's look, and how guests reach
    /// it.</summary>
    public class VenueSettings
    {
        /// <summary>Master volume this venue's displays start at, 0-100.</summary>
        public int DefaultVolume { get; set; } = 100;

        /// <summary>Whether a singer's estimated wait is shown on the queue.</summary>
        public bool ShowEstimatedWaitTime { get; set; } = true;

        /// <summary>Whether tipping is offered at this venue at all.</summary>
        public bool TippingEnabled { get; set; } = true;
        // Off by default: it adds a prompt, so venues opt in rather than inherit one.
        /// <summary>Warns a host when a singer requests a song they already sang recently, within
        /// <see cref="DuplicateSongWindowHours"/>. Off by default.</summary>
        public bool WarnOnDuplicateSong { get; set; }

        /// <summary>How far back, in hours, a repeat request counts as a duplicate. Only read when
        /// <see cref="WarnOnDuplicateSong"/> is on.</summary>
        public int DuplicateSongWindowHours { get; set; } = 4;

        /// <summary>Asks for confirmation before a singer is removed from the queue.</summary>
        public bool PromptBeforeRemovingSinger { get; set; } = true;

        /// <summary>Asks for confirmation before a performance is removed.</summary>
        public bool PromptBeforeRemovingPerformance { get; set; } = true;

        /// <summary>Whether the queue is emptied automatically when the venue's show is closed.</summary>
        public bool ClearQueueOnClose { get; set; } = true;

        // Nullable: EF reads venue rows saved before this key existed as null (initializers
        // are ignored for missing JSON keys); callers fall back to a default config.
        /// <summary>This venue's queue rotation rules. Null means the venue has never set any;
        /// callers fall back to the default rotation config.</summary>
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

        // Both default on, so both were backfilled: EF reads a key missing from a stored row as
        // default and ignores these initializers, which would switch the feature off for every
        // venue that predates it with nothing on screen to say why.

        /// <summary>Whether guests may join from their phones and request songs at all.</summary>
        /// <remarks>Separate from <see cref="QrCodeSource"/>, which only decides whose code the
        /// screen draws: a guest who kept the link from last night does not need the code again,
        /// so taking the code down is not the same as closing the room.</remarks>
        public bool AllowGuestRemote { get; set; } = true;

        /// <summary>Whether a guest who has joined sees the queue, or only their own picks going
        /// in.</summary>
        /// <remarks>Read only wherever it is shown; a guest can never reorder from it. Means
        /// nothing when <see cref="AllowGuestRemote"/> is off, there being no guest to show.
        /// </remarks>
        public bool ShowQueueToGuests { get; set; } = true;

        // Every marquee setting reads "off" when its key is missing, so it needs no backfill.

        /// <summary>Whether the screen carries a marquee at all.</summary>
        public bool MarqueeEnabled { get; set; }

        /// <summary>How many singers ahead the room is shown. Zero is a message-only marquee.</summary>
        public int MarqueeSingerCount { get; set; }

        /// <summary>The venue's own line, e.g. a drink special. Null shows only singers.</summary>
        public string? MarqueeMessage { get; set; }

        /// <summary>Up-next line; tags like <c>{song}</c> replace per singer. Blank uses a default.</summary>
        public string? MarqueeEntryFormat { get; set; }

        /// <summary>Which edge of the screen the marquee band sits against.</summary>
        public MarqueePosition MarqueePosition { get; set; }

        /// <summary>Null takes the screen's own default, which is what most venues want.</summary>
        public string? MarqueeBackgroundColor { get; set; }

        /// <summary>Null takes the screen's own default, which is what most venues want.</summary>
        public string? MarqueeTextColor { get; set; }

        /// <summary>Text height in pixels; zero takes the screen's own size, em-sizing the rest.</summary>
        public int MarqueeFontSizePixels { get; set; }

        /// <summary>Pixels a second; zero takes the screen's own speed, not a lap time.</summary>
        public int MarqueeScrollSpeed { get; set; }

        /// <summary>Pins "Up next" at the leading edge, unscrolled; a modifier, not a style.</summary>
        public bool MarqueePinLabel { get; set; }

        /// <summary>Which of the folder's backgrounds a song may be given, by manifest file name.
        /// </summary>
        /// <remarks>Empty is the black background, which is what a venue that has never been asked
        /// already has — so this needs no separate on/off and no backfill. One name pins every song
        /// to it; several means a different one is picked per render.
        /// <para>File names rather than resolved paths, so a pack that moves between machines keeps
        /// the venue's choices.</para></remarks>
        /// <remarks>Never null, however it arrives. A venue stored before this property existed
        /// deserialises without it, and the initializer alone did not survive that round trip —
        /// which reached every reader as a null list rather than an empty one.</remarks>
        public List<string> SongBackgrounds
        {
            get => _songBackgrounds;
            set => _songBackgrounds = value ?? [];
        }

        private List<string> _songBackgrounds = [];

        /// <summary>Which plugin's QR code shows; null (default) means none shown until chosen.</summary>
        public string? QrCodeSource { get; set; }

        /// <summary>Corner the code sits in; null reads as "no preference", not bottom-right.</summary>
        public OverlayCorner? QrCodeCorner { get; set; }

        /// <summary>How big it is drawn. Null takes medium.</summary>
        public QrCodeSize? QrCodeSize { get; set; }

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
        public OverlayCorner? BreakMusicCardCorner { get; set; }

        /// <summary>An independent copy, including its own <see cref="QueueRotation"/>; changing
        /// one never affects the other.</summary>
        public VenueSettings Clone()
        {
            var clone = (VenueSettings)MemberwiseClone();

            clone.QueueRotation = QueueRotation?.Clone();

            return clone;
        }
    }
}
