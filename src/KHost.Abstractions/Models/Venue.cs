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
        /// <summary>Whether a singer's estimated wait is shown on the queue.</summary>
        public bool ShowEstimatedWaitTime { get; set; } = true;

        /// <summary>Whether tipping is offered at this venue at all.</summary>
        public bool TippingEnabled { get; set; } = true;
        // Off by default: it adds a prompt, so venues opt in rather than inherit one.
        /// <summary>Asks the host to confirm a song that any singer already has queued, or that anyone
        /// sang within <see cref="DuplicateSongWindowHours"/>. Off by default.</summary>
        /// <remarks>A singer asking again for a song they already have queued is refused outright,
        /// whatever this says.</remarks>
        public bool WarnOnDuplicateSong { get; set; }

        /// <summary>How far back, in hours, a song sung earlier counts as a duplicate. Only read when
        /// <see cref="WarnOnDuplicateSong"/> is on.</summary>
        public int DuplicateSongWindowHours { get; set; } = 4;

        /// <summary>Refuses a song another singer already has queued, without asking the host. Off by
        /// default.</summary>
        /// <remarks>Checked before <see cref="WarnOnDuplicateSong"/>, so while it is on that warning
        /// is only ever about a song sung recently.</remarks>
        public bool RefuseSongQueuedForAnotherSinger { get; set; }

        /// <summary>How many songs a singer may have queued for a remote sign-up to be taken. Zero,
        /// the default, is no limit.</summary>
        /// <remarks>Counts every song the singer has queued, however it got there, but refuses only
        /// remote sign-ups: the host can always add one more.</remarks>
        public int RemoteSongLimit { get; set; }

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

        /// <summary>Whether a queued alias is shown. Off when unset, so it needs no backfill.</summary>
        public bool AllowAliases { get; set; }

        // Both default on, so both were backfilled: EF reads a key missing from a stored row as
        // default and ignores these initializers, which would switch the feature off for every
        // venue that predates it with nothing on screen to say why.

        /// <summary>Whether guests may sign up for songs from their phones.</summary>
        /// <remarks>Separate from <see cref="QrCodeSource"/>, which only decides whose code the
        /// screen draws: a guest who kept the link from last night does not need the code again,
        /// so taking the code down is not the same as closing the room.</remarks>
        public bool AllowGuestRemote { get; set; } = true;

        /// <summary>Whether a guest who has joined sees the queue, or only their own picks going
        /// in.</summary>
        /// <remarks>Read only wherever it is shown; a guest can never reorder from it. Independent of
        /// <see cref="AllowGuestRemote"/>: a QR code offering <see cref="QrCodeFeatures.QueueView"/>
        /// stays up while sign-ups are closed.</remarks>
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

        /// <summary>The marquee's band. Null takes the theme's shadow, then the screen's own.</summary>
        public string? MarqueeBackgroundColor { get; set; }

        /// <summary>The marquee's words and its "Up next" label. Null takes the theme's text, then the screen's own.</summary>
        public string? MarqueeTextColor { get; set; }

        /// <summary>Text height in pixels; zero takes the screen's own size, em-sizing the rest.</summary>
        public int MarqueeFontSizePixels { get; set; }

        /// <summary>Pixels a second; zero takes the screen's own speed, not a lap time.</summary>
        public int MarqueeScrollSpeed { get; set; }

        /// <summary>Pins "Up next" at the leading edge, unscrolled; a modifier, not a style.</summary>
        public bool MarqueePinLabel { get; set; }

        /// <summary>Percent of the band that is colour rather than picture; null takes the screen's
        /// own default, which is what most venues want. Unlike the pixel and speed settings above,
        /// zero is a real choice (fully transparent), so it cannot double as "unset".</summary>
        public int? MarqueeBackgroundOpacity { get; set; }

        /// <summary>Singer names in the marquee. Null takes the theme's primary, then the screen's own.</summary>
        public string? MarqueeSingerColor { get; set; }

        /// <summary>Song titles in the marquee. Null takes the theme's highlight, then the screen's own.</summary>
        public string? MarqueeSongColor { get; set; }

        /// <summary>The marquee's divider. Null takes the theme's primary, then a faint marquee text.</summary>
        public string? MarqueeDividerColor { get; set; }

        /// <summary>Dot is the zero value, so an old row defaults there — today's look.</summary>
        public MarqueeDividerShape MarqueeDividerShape { get; set; }

        /// <summary>Takes the marquee down while someone is singing, paused mid-song included, and
        /// puts it back between singers. An ad or an idle screen keeps it. Off when unset.</summary>
        public bool MarqueeHideDuringSong { get; set; }

        /// <summary>The visualisation playlist drawn under a playing song's words when the song has
        /// no picture of its own. Null, or a playlist with no entries, leaves black.</summary>
        /// <remarks>Null when unset, so a venue that has never been asked needs no backfill. Only a
        /// display that draws the words itself shows one; burned-in words stay on black.</remarks>
        public Guid? VisualisationPlaylistId { get; set; }

        /// <summary>What the "Up next" card is drawn over. Over when unset.</summary>
        public NextSingerBackground NextSingerBackground { get; set; }

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

        // The venue's theme: four colours every screen colour below falls back to when the venue
        // leaves its own unset. Each is #rrggbb, or null for none; a colour with neither its own
        // setting nor its theme colour takes the screen's own.

        /// <summary>The theme's main colour: the marquee divider, and the main colour of a
        /// visualisation that respects the venue's theme.</summary>
        public string? ThemePrimaryColor { get; set; }

        /// <summary>The theme's accent: sung words, singer names, and a respecting visualisation's
        /// light colour.</summary>
        public string? ThemeHighlightColor { get; set; }

        /// <summary>The theme's text colour: unsung words and the text of every card and band.</summary>
        public string? ThemeTextColor { get; set; }

        /// <summary>The theme's dark colour: the screen's background, outlines, the panels behind
        /// cards and bands, and a respecting visualisation's dark colour.</summary>
        public string? ThemeShadowColor { get; set; }

        /// <summary>Behind everything the screen draws. Null takes the theme's shadow.</summary>
        public string? ScreenBackgroundColor { get; set; }

        /// <summary>Sung words, where the song's timing names no colour. Null takes the theme's highlight.</summary>
        public string? LyricsSungColor { get; set; }

        /// <summary>Words not yet sung, where the timing names no colour. Null takes the theme's text.</summary>
        public string? LyricsUnsungColor { get; set; }

        /// <summary>The edge round every word. Null takes the theme's shadow.</summary>
        public string? LyricsOutlineColor { get; set; }

        /// <summary>The title card a song opens on. Null takes the theme's text.</summary>
        public string? IntroTextColor { get; set; }

        /// <summary>The edge round the title card's words. Null takes the theme's shadow.</summary>
        public string? IntroOutlineColor { get; set; }

        /// <summary>The "Up next" card's words. Null takes the theme's text.</summary>
        public string? NextSingerTextColor { get; set; }

        /// <summary>The singer's name on the "Up next" card. Null takes the theme's highlight.</summary>
        public string? NextSingerNameColor { get; set; }

        /// <summary>The panel behind the "Up next" card. Null takes the theme's shadow.</summary>
        public string? NextSingerPanelColor { get; set; }

        /// <summary>The words of the card naming what plays between singers. Null takes the theme's text.</summary>
        public string? BreakMusicCardTextColor { get; set; }

        /// <summary>The panel behind that card. Null takes the theme's shadow.</summary>
        public string? BreakMusicCardBackgroundColor { get; set; }

        /// <summary>The frame round a QR code, its quiet zone: keep it light, or phones may not read
        /// the code. Null takes the theme's text.</summary>
        public string? QrCodeFrameColor { get; set; }

        /// <summary>The words under a QR code, on its frame. Null takes the theme's shadow.</summary>
        public string? QrCodeCaptionColor { get; set; }

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
