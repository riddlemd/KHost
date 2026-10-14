using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace KHost.UserInterface.Services;

/// <summary>One setting the App Settings page edits: where it lives in configuration, how it reads
/// back (default and normaliser included), and what the overlay holds for it.</summary>
internal abstract class AppSettingDescriptor(string? section, string key, bool omitWhenNull)
{
    /// <summary>The overlay object holding <see cref="Key"/>; null for a key at the overlay's root.</summary>
    public string? Section { get; } = section;

    public string Key { get; } = key;

    /// <summary>Left out of the overlay entirely, rather than written as null, when it has no value.</summary>
    public bool OmitWhenNull { get; } = omitWhenNull;

    public string Path => Section is null ? Key : $"{Section}:{Key}";

    public abstract void ReadInto(IConfiguration configuration, AppSettings settings);

    /// <summary>The overlay value for <paramref name="settings"/>; its runtime type decides the JSON
    /// written (an int stays a number, a duration a string).</summary>
    public abstract object? Write(AppSettings settings);
}

internal sealed class AppSettingDescriptor<T> : AppSettingDescriptor
{
    private readonly PropertyInfo _property;
    private readonly Func<IConfiguration, string, T> _read;
    private readonly Func<T, object?> _write;

    public AppSettingDescriptor(
        string? section,
        string key,
        Expression<Func<AppSettings, T>> property,
        Func<IConfiguration, string, T> read,
        Func<T, object?> write,
        bool omitWhenNull = false)
        : base(section, key, omitWhenNull)
    {
        // One lambda names the property for both directions, so the getter and setter cannot disagree.
        _property = (property.Body as MemberExpression)?.Member as PropertyInfo
            ?? throw new ArgumentException("Must name an AppSettings property", nameof(property));
        _read = read;
        _write = write;
    }

    public override void ReadInto(IConfiguration configuration, AppSettings settings)
        => _property.SetValue(settings, _read(configuration, Path));

    public override object? Write(AppSettings settings)
        => _write((T)_property.GetValue(settings)!);
}
