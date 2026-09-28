using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>Moves apart the colours of timed lyrics that a colour-blind viewer would take for one
/// another: a singer's sung and unsung words, and two singers' words on screen together.</summary>
/// <remarks>Applied to the timing itself, so the screen and the burn-in draw the same colours from
/// the same data. A song with nothing at risk comes back as the same instance, and a colour that is
/// fine keeps its hue: only lightness moves, so a duet still looks like the one its media chose.</remarks>
public static class ColorBlindSafeLyrics
{
    /// <summary>Below this CIEDE2000 difference, under some sight, two colours may be taken for one.</summary>
    public const double RiskDeltaE = 10;

    /// <summary>Below this WCAG contrast, lightness alone does not tell two colours apart.</summary>
    public const double RiskContrast = 1.5;

    /// <summary>A fix aims past the risk line, so a moved pair is not left on its edge.</summary>
    public const double FixedDeltaE = 12;

    /// <inheritdoc cref="FixedDeltaE"/>
    public const double FixedContrast = 1.6;

    /// <summary>No colour is darkened below this luminance: 4.5:1 against the black outline.</summary>
    public const double LuminanceFloor = 0.175;

    private const int MaxPasses = 4;
    private const int MaxSteps = 200;
    private const double Step = 0.01;
    private const double FallbackTintLightness = 0.93;

    // The drawers' own fallbacks: a page naming no colour is seen in these.
    internal static readonly LyricColor ThemeActive = new(0x85, 0x58, 0xFA);
    internal static readonly LyricColor ThemeInactive = new(0xFF, 0xFF, 0xFF);

    // Okabe–Ito: the categorical palette that stays distinct under every common deficiency.
    internal static readonly LyricColor[] OkabeIto =
    [
        new(0x00, 0x72, 0xB2), new(0xE6, 0x9F, 0x00), new(0xCC, 0x79, 0xA7), new(0x00, 0x9E, 0x73),
        new(0x56, 0xB4, 0xE9), new(0xD5, 0x5E, 0x00), new(0xF0, 0xE4, 0x42),
    ];

    private enum Job { Wipe, Sung, Unsung }

    private readonly record struct Constraint(Job Job, string X, int XIndex, string Y, int YIndex);

    /// <summary>The lyrics with any colours at risk moved apart, or <paramref name="lyrics"/>
    /// itself when none are.</summary>
    public static TimedLyrics Separate(TimedLyrics lyrics)
    {
        // A voice is known by the colours of its first page; the media gives each singer one pair.
        var voices = new List<string>();
        var original = new Dictionary<string, (LyricColor Active, LyricColor Inactive)>(StringComparer.Ordinal);
        foreach (var page in lyrics.Pages)
        {
            var voice = page.Voice ?? "";
            if (original.TryAdd(voice, Resolve(page.Active, page.Inactive))) voices.Add(voice);
        }

        var together = new List<(string, string)>();
        for (var i = 0; i < lyrics.Pages.Count; i++)
        for (var j = i + 1; j < lyrics.Pages.Count; j++)
        {
            LyricPage x = lyrics.Pages[i], y = lyrics.Pages[j];
            string vx = x.Voice ?? "", vy = y.Voice ?? "";
            if (vx == vy || x.ShowFromSeconds >= y.ShowUntilSeconds || y.ShowFromSeconds >= x.ShowUntilSeconds) continue;

            var pair = string.CompareOrdinal(vx, vy) < 0 ? (vx, vy) : (vy, vx);
            if (!together.Contains(pair)) together.Add(pair);
        }

        var palette = Choose(voices, original, together);
        if (palette is null) return lyrics;

        var changed = false;
        var pages = new List<LyricPage>(lyrics.Pages.Count);
        foreach (var page in lyrics.Pages)
        {
            var voice = page.Voice ?? "";
            var moved = Rewrite(page.Active, page.Inactive, original[voice], palette[voice]);
            if (moved is { } colors && (colors.Active != page.Active || colors.Inactive != page.Inactive))
            {
                pages.Add(page with { Active = colors.Active, Inactive = colors.Inactive });
                changed = true;
            }
            else pages.Add(page);
        }

        // A count-in carries no voice; it is the voice whose page pair it repeats.
        var countIns = new List<LyricCountIn>(lyrics.CountIns.Count);
        foreach (var countIn in lyrics.CountIns)
        {
            var own = Resolve(countIn.Active, countIn.Inactive);
            var voice = voices.FirstOrDefault(v => original[v] == own);
            var moved = voice is null ? null : Rewrite(countIn.Active, countIn.Inactive, original[voice], palette[voice]);
            if (moved is { } colors && (colors.Active != countIn.Active || colors.Inactive != countIn.Inactive))
            {
                countIns.Add(countIn with { Active = colors.Active, Inactive = colors.Inactive });
                changed = true;
            }
            else countIns.Add(countIn);
        }

        return changed ? lyrics with { Pages = pages, CountIns = countIns } : lyrics;
    }

    /// <summary>Whether a viewer with any of the simulated sights may take one colour for the other.</summary>
    internal static bool IsAtRisk(LyricColor a, LyricColor b) => AnySightBelow(a, b, RiskDeltaE, RiskContrast);

    /// <summary>Lightens <paramref name="up"/> to white first, then darkens <paramref name="down"/>
    /// towards <see cref="LuminanceFloor"/>, hue kept, until the pair clears the fixed line or
    /// neither can move.</summary>
    internal static (LyricColor Up, LyricColor Down) PushApart(LyricColor up, LyricColor down)
    {
        double lightUp = ColorVision.Lightness(up), lightDown = ColorVision.Lightness(down);
        LyricColor newUp = up, newDown = down;

        for (var i = 0; i < MaxSteps; i++)
        {
            if (!AnySightBelow(newUp, newDown, FixedDeltaE, FixedContrast)) break;

            if (lightUp + Step <= 1.0)
            {
                lightUp += Step;
                newUp = ColorVision.WithLightness(up, lightUp);
            }
            else if (ColorVision.Luminance(ColorVision.WithLightness(down, lightDown - Step)) >= LuminanceFloor)
            {
                lightDown -= Step;
                newDown = ColorVision.WithLightness(down, lightDown);
            }
            else break;
        }

        return (newUp, newDown);
    }

    /// <summary>Okabe–Ito by voice order, each with a pale tint of its own hue for unsung words.</summary>
    internal static Dictionary<string, LyricColor[]> Fallback(IReadOnlyList<string> voices) =>
        voices
            .Select((voice, n) => (voice, color: OkabeIto[n % OkabeIto.Length]))
            .ToDictionary(v => v.voice, v => new[] { v.color, ColorVision.WithLightness(v.color, FallbackTintLightness) }, StringComparer.Ordinal);

    /// <summary>Each voice's [Active, Inactive] after the fix, or null when nothing was at risk.</summary>
    private static Dictionary<string, LyricColor[]>? Choose(
        List<string> voices,
        Dictionary<string, (LyricColor Active, LyricColor Inactive)> original,
        List<(string, string)> together)
    {
        var palette = voices.ToDictionary(v => v, v => new[] { original[v].Active, original[v].Inactive }, StringComparer.Ordinal);
        Constraint[] constraints =
        [
            .. voices.Select(v => new Constraint(Job.Wipe, v, 0, v, 1)),
            .. together.SelectMany(p => new[] { new Constraint(Job.Sung, p.Item1, 0, p.Item2, 0), new Constraint(Job.Unsung, p.Item1, 1, p.Item2, 1) }),
        ];

        bool AtRisk(Constraint c) => IsAtRisk(palette[c.X][c.XIndex], palette[c.Y][c.YIndex]);

        if (!constraints.Any(AtRisk)) return null;

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            foreach (var c in constraints)
            {
                if (!AtRisk(c)) continue;

                var ((upVoice, upIndex), (downVoice, downIndex)) = c.Job switch
                {
                    // Unsung words brighten; the sung colour gives way, as the saturated one of the two.
                    Job.Wipe => ((c.Y, 1), (c.X, 0)),
                    // The tint that darkens is the voice whose own sung colour is darker: most room above its wipe.
                    Job.Unsung => Order(ColorVision.Luminance(palette[c.X][0]), c.X, ColorVision.Luminance(palette[c.Y][0]), c.Y) > 0
                        ? ((c.X, 1), (c.Y, 1))
                        : ((c.Y, 1), (c.X, 1)),
                    _ => Order(ColorVision.Luminance(palette[c.X][0]), c.X, ColorVision.Luminance(palette[c.Y][0]), c.Y) >= 0
                        ? ((c.X, 0), (c.Y, 0))
                        : ((c.Y, 0), (c.X, 0)),
                };

                var (up, down) = PushApart(palette[upVoice][upIndex], palette[downVoice][downIndex]);
                palette[upVoice][upIndex] = up;
                palette[downVoice][downIndex] = down;
            }

            if (!constraints.Any(AtRisk)) return palette;
        }

        return Fallback(voices);
    }

    /// <summary>Luminance first, then the voice's name, so equal colours still pick the same one every time.</summary>
    private static int Order(double luminanceA, string voiceA, double luminanceB, string voiceB) =>
        luminanceA != luminanceB ? luminanceA.CompareTo(luminanceB) : string.CompareOrdinal(voiceA, voiceB);

    private static bool AnySightBelow(LyricColor a, LyricColor b, double deltaE, double contrast) =>
        ColorVision.EverySight.Any(sight =>
        {
            LyricColor sa = ColorVision.Simulate(a, sight), sb = ColorVision.Simulate(b, sight);
            return ColorVision.DeltaE2000(sa, sb) < deltaE && ColorVision.Contrast(sa, sb) < contrast;
        });

    private static (LyricColor Active, LyricColor Inactive) Resolve(LyricColor? active, LyricColor? inactive) =>
        (active ?? ThemeActive, inactive ?? ThemeInactive);

    /// <summary>The new pair for colours that repeat a voice's own, or null for colours that do not.</summary>
    /// <remarks>An unset colour that did not move stays unset, so the theme still owns it.</remarks>
    private static (LyricColor? Active, LyricColor? Inactive)? Rewrite(
        LyricColor? active, LyricColor? inactive, (LyricColor Active, LyricColor Inactive) original, LyricColor[] chosen)
    {
        if (Resolve(active, inactive) != original) return null;

        return (chosen[0] == original.Active ? active : chosen[0],
                chosen[1] == original.Inactive ? inactive : chosen[1]);
    }
}
