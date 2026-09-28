using KHost.Abstractions.Models;

namespace KHost.Domain.Services;

/// <summary>How two colours look side by side to viewers with and without colour-vision
/// deficiencies, and how to move a colour's lightness while keeping its hue.</summary>
/// <remarks>Simulation is Machado, Oliveira and Fernandes (2009) applied in linear RGB; difference
/// is CIEDE2000 over CIELAB (D65); contrast is WCAG 2's ratio; lightness moves in OKLab. Every
/// operation is pure, so the same colours give the same answer on every machine.</remarks>
internal static class ColorVision
{
    /// <summary>The ways a colour is seen, normal vision first.</summary>
    internal enum Sight { Normal, Protanopia, Deuteranopia, Tritanopia, Protanomaly, Deuteranomaly }

    internal static readonly Sight[] EverySight = Enum.GetValues<Sight>();

    // Anomalies at severity 0.6: a typical anomalous trichromat, well short of the full dichromat.
    private static readonly double[][][] Machado =
    [
        [[1, 0, 0], [0, 1, 0], [0, 0, 1]],
        [[0.152286, 1.052583, -0.204868], [0.114503, 0.786281, 0.099216], [-0.003882, -0.048116, 1.051998]],
        [[0.367322, 0.860646, -0.227968], [0.280085, 0.672501, 0.047413], [-0.011820, 0.042940, 0.968881]],
        [[1.255528, -0.076749, -0.178779], [-0.078411, 0.930809, 0.147602], [0.004733, 0.691367, 0.303900]],
        [[0.385450, 0.769005, -0.154455], [0.100526, 0.829802, 0.069673], [-0.007671, -0.022539, 1.030210]],
        [[0.547494, 0.607765, -0.155259], [0.181692, 0.781742, 0.036566], [-0.010410, 0.027275, 0.983136]],
    ];

    internal static LyricColor Simulate(LyricColor color, Sight sight)
    {
        if (sight == Sight.Normal) return color;

        var m = Machado[(int)sight];
        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        return new LyricColor(
            Encode(m[0][0] * r + m[0][1] * g + m[0][2] * b),
            Encode(m[1][0] * r + m[1][1] * g + m[1][2] * b),
            Encode(m[2][0] * r + m[2][1] * g + m[2][2] * b));
    }

    /// <summary>WCAG relative luminance, 0 for black to 1 for white.</summary>
    internal static double Luminance(LyricColor color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    /// <summary>WCAG contrast ratio, 1 for equal luminance to 21 for black on white.</summary>
    internal static double Contrast(LyricColor a, LyricColor b)
    {
        double ya = Luminance(a), yb = Luminance(b);
        return (Math.Max(ya, yb) + 0.05) / (Math.Min(ya, yb) + 0.05);
    }

    internal static (double L, double A, double B) Lab(LyricColor color)
    {
        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;

        static double F(double t) => t > 216.0 / 24389 ? Math.Pow(t, 1.0 / 3) : (24389.0 / 27 * t + 16) / 116;

        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    /// <summary>CIEDE2000 colour difference; about 2 is the least a viewer notices between patches
    /// that touch.</summary>
    internal static double DeltaE2000(LyricColor first, LyricColor second) => DeltaE2000(Lab(first), Lab(second));

    /// <inheritdoc cref="DeltaE2000(LyricColor, LyricColor)"/>
    internal static double DeltaE2000((double L, double A, double B) first, (double L, double A, double B) second)
    {
        var (l1, a1, b1) = first;
        var (l2, a2, b2) = second;
        const double pow25To7 = 6103515625.0;

        double c1 = Hypot(a1, b1), c2 = Hypot(a2, b2), cBar = (c1 + c2) / 2;
        var g = 0.5 * (1 - Math.Sqrt(Math.Pow(cBar, 7) / (Math.Pow(cBar, 7) + pow25To7)));
        double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
        double c1p = Hypot(a1p, b1), c2p = Hypot(a2p, b2);
        double h1 = Wrap360(Degrees(Math.Atan2(b1, a1p))), h2 = Wrap360(Degrees(Math.Atan2(b2, a2p)));

        double dL = l2 - l1, dC = c2p - c1p, dh;
        if (c1p * c2p == 0) dh = 0;
        else
        {
            dh = h2 - h1;
            if (dh > 180) dh -= 360;
            else if (dh < -180) dh += 360;
        }
        var dH = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Radians(dh / 2));

        double lBar = (l1 + l2) / 2, cBarP = (c1p + c2p) / 2, hBar;
        if (c1p * c2p == 0) hBar = h1 + h2;
        else if (Math.Abs(h1 - h2) <= 180) hBar = (h1 + h2) / 2;
        else hBar = h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;

        var t = 1 - 0.17 * Math.Cos(Radians(hBar - 30)) + 0.24 * Math.Cos(Radians(2 * hBar))
                + 0.32 * Math.Cos(Radians(3 * hBar + 6)) - 0.20 * Math.Cos(Radians(4 * hBar - 63));
        var dTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
        var rc = 2 * Math.Sqrt(Math.Pow(cBarP, 7) / (Math.Pow(cBarP, 7) + pow25To7));
        var sl = 1 + 0.015 * Math.Pow(lBar - 50, 2) / Math.Sqrt(20 + Math.Pow(lBar - 50, 2));
        var sc = 1 + 0.045 * cBarP;
        var sh = 1 + 0.015 * cBarP * t;
        var rt = -Math.Sin(Radians(2 * dTheta)) * rc;

        return Math.Sqrt(Math.Pow(dL / sl, 2) + Math.Pow(dC / sc, 2) + Math.Pow(dH / sh, 2) + rt * (dC / sc) * (dH / sh));
    }

    /// <summary>OKLab lightness, 0 for black to 1 for white.</summary>
    internal static double Lightness(LyricColor color) => OkLab(color).L;

    /// <summary>The same hue at OKLab lightness <paramref name="lightness"/>, with chroma given up only
    /// as far as sRGB needs.</summary>
    internal static LyricColor WithLightness(LyricColor color, double lightness)
    {
        var (_, a, b) = OkLab(color);
        var chroma = Hypot(a, b);
        var hue = Math.Atan2(b, a);

        // Bisected a fixed number of times rather than to a tolerance, so the answer never depends
        // on how close the last step happened to land.
        double lo = 0, hi = chroma;
        for (var i = 0; i < 30; i++)
        {
            var mid = (lo + hi) / 2;
            var (r, g, bl) = OkLabToLinear(lightness, mid * Math.Cos(hue), mid * Math.Sin(hue));
            if (InGamut(r) && InGamut(g) && InGamut(bl)) lo = mid;
            else hi = mid;
        }

        var (fr, fg, fb) = OkLabToLinear(lightness, lo * Math.Cos(hue), lo * Math.Sin(hue));
        return new LyricColor(Encode(fr), Encode(fg), Encode(fb));
    }

    private static (double L, double A, double B) OkLab(LyricColor color)
    {
        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        var l = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 1.0 / 3);
        var m = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 1.0 / 3);
        var s = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b, 1.0 / 3);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (double R, double G, double B) OkLabToLinear(double lightness, double a, double b)
    {
        var l = Math.Pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(lightness - 0.0894841775 * a - 1.2914855480 * b, 3);
        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
    }

    private static bool InGamut(double channel) => channel >= -1e-6 && channel <= 1 + 1e-6;

    private static double Linear(byte channel)
    {
        var v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double linear)
    {
        var v = Math.Min(1, Math.Max(0, linear));
        return (byte)Math.Round(255 * (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055));
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double Degrees(double radians) => radians * (180.0 / Math.PI);

    private static double Radians(double degrees) => degrees * (Math.PI / 180.0);

    private static double Wrap360(double degrees)
    {
        var wrapped = degrees % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }
}
