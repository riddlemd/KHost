using System;
using System.Collections.Generic;
using System.Text;

namespace KHost.Common.Chronography
{
    /// <summary>Formatting a duration the way the console shows it, rather than a raw <see cref="TimeSpan"/>.</summary>
    public static class TimeSpanExtensions
    {
        /// <summary>Formats as "MM:SS", total minutes rather than rolling over into hours.</summary>
        public static string ToTotalMinutesAndSeconds(this TimeSpan value)
            => $"{(int)Math.Floor(value.TotalMinutes):D2}:{value.Seconds:D2}";
    }
}
