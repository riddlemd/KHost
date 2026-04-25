using System;
using System.Collections.Generic;
using System.Text;

namespace KHost.Domain.Chronography
{
    public static class TimeSpanExtensions
    {
        public static string ToTotalMinutesAndSeconds(this TimeSpan value)
            => $"{(int)Math.Floor(value.TotalMinutes):D2}:{value.Seconds:D2}";
    }
}
