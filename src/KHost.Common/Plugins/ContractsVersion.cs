using System.Globalization;
using System.Reflection;
using KHost.Abstractions.Models.Plugins;

namespace KHost.Common.Plugins;

/// <summary>A version of the published contracts (<c>KHost.Abstractions</c> and <c>KHost.Common</c>),
/// ordered as semantic versioning orders it. A new major is a break; a new minor or patch is not.</summary>
public readonly record struct ContractsVersion : IComparable<ContractsVersion>
{
    private const string MetadataKey = "ContractsVersion";

    private static readonly Lazy<ContractsVersion> ThisHostVersion = new(ReadThisHost);

    /// <summary>Creates a version from its parts.</summary>
    /// <param name="major">Moves on a break.</param>
    /// <param name="minor">Moves on an addition.</param>
    /// <param name="patch">Moves on a fix with no change of shape.</param>
    /// <param name="prerelease">The dot-separated prerelease label, or null for a release.</param>
    public ContractsVersion(int major, int minor, int patch, string? prerelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    /// <summary>The contracts this host was built with; a plugin needing a version this does not
    /// <see cref="Covers"/> is refused.</summary>
    public static ContractsVersion ThisHost => ThisHostVersion.Value;

    /// <summary>The oldest contracts any released plugin was built against, and the surface 1.0.0
    /// grew from: a 1.x host runs a plugin needing any 0.61.x.</summary>
    public static ContractsVersion Legacy { get; } = new(0, 61, 0);

    /// <summary>Moves on a break.</summary>
    public int Major { get; }

    /// <summary>Moves on an addition.</summary>
    public int Minor { get; }

    /// <summary>Moves on a fix with no change of shape.</summary>
    public int Patch { get; }

    /// <summary>The prerelease label (the part after <c>-</c>), or null for a release. A prerelease
    /// orders before the release of the same number.</summary>
    public string? Prerelease { get; }

    /// <summary>Reads <c>Major.Minor.Patch</c>, optionally followed by <c>-prerelease</c> and
    /// <c>+build</c>; the build label is ignored.</summary>
    /// <exception cref="FormatException"><paramref name="value"/> is not a semantic version.</exception>
    public static ContractsVersion Parse(string value)
        => TryParse(value, out var version) ? version : throw new FormatException($"'{value}' is not a contracts version (Major.Minor.Patch).");

    /// <summary>Reads a version as <see cref="Parse"/> does; false for anything else, including null.</summary>
    public static bool TryParse(string? value, out ContractsVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        var plus = text.IndexOf('+');

        if (plus >= 0)
        {
            if (!IsDottedIdentifiers(text[(plus + 1)..], numericLeadingZerosAllowed: true))
                return false;

            text = text[..plus];
        }

        string? prerelease = null;
        var dash = text.IndexOf('-');

        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];

            if (!IsDottedIdentifiers(prerelease, numericLeadingZerosAllowed: false))
                return false;

            text = text[..dash];
        }

        var parts = text.Split('.');

        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
            return false;

        version = new ContractsVersion(major, minor, patch, prerelease);

        return true;
    }

    /// <summary>True when a plugin needing <paramref name="required"/> runs on a host with these
    /// contracts: the same major (a 0.61.x requirement counts as major 1), and no newer than this
    /// version, prerelease and patch included.</summary>
    public bool Covers(ContractsVersion required) => IsSameLine(required) && required <= this;

    /// <summary>Why a plugin needing <paramref name="required"/> does not run here, in words a host
    /// can act on: whether KHost or the plugin needs the update. Null when it runs.</summary>
    public string? DescribeRefusal(ContractsVersion required)
    {
        if (Covers(required))
            return null;

        if (required > this)
            return $"Needs a newer KHost (needs contracts {required}; this KHost has {this}).";

        return $"Built for an older KHost (needs contracts {required}; this KHost has {this}); it needs an update.";
    }

    /// <inheritdoc />
    public int CompareTo(ContractsVersion other)
    {
        var byNumber = Major != other.Major ? Major.CompareTo(other.Major)
            : Minor != other.Minor ? Minor.CompareTo(other.Minor)
            : Patch.CompareTo(other.Patch);

        if (byNumber != 0)
            return byNumber;

        if (Prerelease is null || other.Prerelease is null)
            return (Prerelease is null).CompareTo(other.Prerelease is null);

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>The version as <see cref="Parse"/> reads it, without any build label.</summary>
    public override string ToString()
        => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    /// <summary>True when <paramref name="left"/> orders before <paramref name="right"/>.</summary>
    public static bool operator <(ContractsVersion left, ContractsVersion right) => left.CompareTo(right) < 0;

    /// <summary>True when <paramref name="left"/> orders after <paramref name="right"/>.</summary>
    public static bool operator >(ContractsVersion left, ContractsVersion right) => left.CompareTo(right) > 0;

    /// <summary>True when <paramref name="left"/> orders before or level with <paramref name="right"/>.</summary>
    public static bool operator <=(ContractsVersion left, ContractsVersion right) => left.CompareTo(right) <= 0;

    /// <summary>True when <paramref name="left"/> orders after or level with <paramref name="right"/>.</summary>
    public static bool operator >=(ContractsVersion left, ContractsVersion right) => left.CompareTo(right) >= 0;

    // 1.0.0 only added to 0.61 and removed PluginApi, which no plugin calls at run time.
    private bool IsSameLine(ContractsVersion required)
        => required.Major == Major || (Major == 1 && required.Major == Legacy.Major && required.Minor == Legacy.Minor);

    private static ContractsVersion ReadThisHost()
    {
        var value = typeof(PluginManifest).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataKey)?.Value;

        return TryParse(value, out var version)
            ? version
            : throw new InvalidOperationException($"KHost.Abstractions carries no readable {MetadataKey} metadata ('{value}').");
    }

    private static bool TryParseNumber(string text, out int number)
    {
        number = 0;

        if (text.Length == 0 || (text.Length > 1 && text[0] == '0') || !text.All(char.IsAsciiDigit))
            return false;

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsDottedIdentifiers(string text, bool numericLeadingZerosAllowed)
        => text.Length > 0 && text.Split('.').All(identifier =>
            identifier.Length > 0
            && identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && (numericLeadingZerosAllowed || !IsNumeric(identifier) || identifier.Length == 1 || identifier[0] != '0'));

    private static bool IsNumeric(string identifier) => identifier.All(char.IsAsciiDigit);

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');

        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var a = leftParts[i];
            var b = rightParts[i];
            var aNumeric = IsNumeric(a);
            var bNumeric = IsNumeric(b);

            var byPart = (aNumeric, bNumeric) switch
            {
                (true, true) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b),
                // Numeric identifiers order before alphanumeric ones.
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };

            if (byPart != 0)
                return Math.Sign(byPart);
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
