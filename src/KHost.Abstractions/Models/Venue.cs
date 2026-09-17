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

        /// <summary>
        /// Whether a name queued alongside a song is honoured. A guest on a song-first remote types
        /// a nickname per pick, and a venue that would rather see the singer it knows leaves this
        /// off — the name is recorded either way, so turning it on later shows what was already
        /// there rather than starting from nothing.
        ///
        /// Off for a venue that has never been asked, which is why it needs no backfill.
        /// </summary>
        public bool AllowAliases { get; set; }

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

        /// <summary>
        /// How each up-next line reads. Tags <c>{song}</c>, <c>{artist}</c>, <c>{singer}</c> and
        /// <c>{position}</c> are replaced per singer; null or blank falls back to
        /// <c>"{song} - {singer}"</c> rather than composing an empty line.
        /// </summary>
        public string? MarqueeEntryFormat { get; set; }

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

        /// <summary>
        /// The one plugin whose QR code this venue shows, by plugin id, or null for none. Null is
        /// also the default, and deliberately: a code invites a room to scan it, and which one
        /// that is belongs to whoever runs the venue rather than to whichever plugin happened to
        /// register first. Plugins declare themselves as sources in their manifests; this names
        /// the one that is taken up.
        ///
        /// A stored id whose plugin is no longer loaded still reads back, so the dialog can say so
        /// rather than silently falling back — the same shape as <see cref="BreakMusicProvider"/>.
        /// </summary>
        public string? QrCodeSource { get; set; }

        /// <summary>
        /// Which corner the code sits in. Nullable rather than the enum's first member, so a venue
        /// that has never been asked reads as "no preference" instead of silently meaning
        /// bottom-right — the same reason the marquee's sizes use zero.
        /// </summary>
        public ScreenCorner? QrCodeCorner { get; set; }

        /// <summary>How big it is drawn. Null takes medium.</summary>
        public ScreenQrSize? QrCodeSize { get; set; }

        /// <summary>
        /// Keeps the picture clean while someone is singing. Off for a venue that has never been
        /// asked, since a code that vanishes mid-song is the surprising half of the pair.
        /// </summary>
        public bool QrCodeHideDuringSong { get; set; }

        /// <summary>
        /// How much white sits around the code, counted in its own modules. The standard is four,
        /// which is a quarter of the code's width again and reads as a slab over video; one is
        /// enough on a lit panel. Counted in modules rather than pixels because that is what a
        /// scanner measures — the margin has to keep its proportion whatever size the code is drawn.
        ///
        /// Zero means the screen's own, the same way the marquee's sizes do: a venue that has never
        /// been asked has no opinion, and a number input cannot show "unset" any other way.
        /// </summary>
        public int QrCodeSafeZone { get; set; }

        /// <summary>
        /// How far in from the screen's edges the code sits, as a percentage of the shorter side.
        /// A percentage rather than pixels because a venue may run screens of different sizes off
        /// one host, and a corner that looks right on the television should not drift on the
        /// projector beside it.
        ///
        /// Zero means the screen's own. Its own is not flush: televisions overscan and projectors
        /// are rarely framed exactly, so a hair of inset is the difference between a code in the
        /// corner and a code with its edge cut off the picture.
        /// </summary>
        public double QrCodeOffset { get; set; }

        /// <summary>
        /// How the placeholder image fills the screen, or null to take the image's own answer.
        /// </summary>
        /// <remarks>
        /// An override rather than the only setting. Scaling belongs to a picture — a wide banner
        /// and a portrait poster want opposite answers on the same television — so the media row
        /// keeps its own, and this is for the venue that disagrees. Null by default, which is the
        /// image's answer and therefore no change to any venue that never sets it.
        /// </remarks>
        public ImageScaling? BrandingImageScaling { get; set; }

        /// <summary>
        /// Whether the screen names what is playing between singers. Off for a venue that has
        /// never been asked, so the key missing from a stored row reads as off and the feature
        /// needs no backfill — the same shape as every marquee setting.
        /// </summary>
        public bool BreakMusicCardEnabled { get; set; }

        /// <summary>
        /// Which corner names it. Null takes bottom-left rather than the QR code's bottom-right:
        /// they stack rather than cover each other when they share a corner, but a venue that has
        /// expressed no opinion is better served by them not sharing one at all.
        /// </summary>
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
