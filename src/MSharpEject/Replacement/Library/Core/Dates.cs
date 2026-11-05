using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Platform
{
    public static partial class Extensions
    {
        public static bool IsOlderThan(this DateTime @this, TimeSpan span) => DateTime.Now - @this > span;

        public static bool IsNewerThan(this DateTime source, TimeSpan span) => DateTime.Now - source < span;

        public static bool IsAfter(this DateTime @this, DateTime otherDate) => @this > otherDate;

        public static bool IsBefore(this DateTime @this, DateTime otherDate) => @this < otherDate;

        public static bool IsAfterOrEqualTo(this DateTime @this, DateTime otherDate) => @this >= otherDate;

        public static bool IsBeforeOrEqualTo(this DateTime @this, DateTime otherDate) => @this <= otherDate;

        public static bool IsBetween(this DateTime @this, DateTime from, DateTime to, bool includingEdges = true) =>
            includingEdges ? @this >= from && @this <= to : @this > from && @this < to;

        public static bool IsToday(this DateTime date) => date.Date == DateTime.Today;

        public static bool IsTodayOrFuture(this DateTime @this) => @this.Date >= DateTime.Today;

        public static bool IsInTheFuture(this DateTime @this) => @this > DateTime.Now;

        public static bool IsInThePast(this DateTime date) => date < DateTime.Now;

        public static bool IsWeekend(this DateTime @this) => @this.DayOfWeek == DayOfWeek.Saturday || @this.DayOfWeek == DayOfWeek.Sunday;

        public static bool IsInSameWeek(this DateTime day, DateTime other) => day.GetBeginningOfWeek() == other.GetBeginningOfWeek();

        public static bool IsInSameMonth(this DateTime @this, DateTime other) => @this.Year == other.Year && @this.Month == other.Month;

        public static int DaysInYear(this DateTime @this) => DateTime.IsLeapYear(@this.Year) ? 366 : 365;

        public static int DaysInMonth(this DateTime @this) => DateTime.DaysInMonth(@this.Year, @this.Month);

        public static DateTime GetBeginningOfWeek(this DateTime @this)
        {
            var diff = (7 + (@this.DayOfWeek - DayOfWeek.Monday)) % 7;
            return @this.Date.AddDays(-diff);
        }

        public static DateTime GetEndOfWeek(this DateTime date, DayOfWeek startOfWeek = DayOfWeek.Monday)
        {
            var diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
            return date.Date.AddDays(-diff).AddDays(7).AddTicks(-1);
        }

        public static DateTime GetBeginningOfMonth(this DateTime day) => new DateTime(day.Year, day.Month, 1);

        public static DateTime GetEndOfMonth(this DateTime date) => date.GetBeginningOfMonth().AddMonths(1).AddTicks(-1);

        public static bool IsLastDayOfMonth(this DateTime @this) => @this.Day == @this.DaysInMonth();

        public static DateTime GetBeginningOfQuarter(this DateTime @this) => new DateTime(@this.Year, (@this.Month - 1) / 3 * 3 + 1, 1);

        public static DateTime GetEndOfQuarter(this DateTime @this) => @this.GetBeginningOfQuarter().AddMonths(3).AddTicks(-1);

        public static DateTime GetEndOfYear(this DateTime @this) => new DateTime(@this.Year, 12, 31).EndOfDay();

        public static DateTime EndOfDay(this DateTime date) => date.Date.AddDays(1).AddTicks(-1);

        public static DateTime? EndOfDay(this DateTime? @this) => @this?.EndOfDay();

        public static DateTime AddWeeks(this DateTime @this, int numberOfWeeks) => @this.AddDays(numberOfWeeks * 7);

        public static DateTime RoundToSecond(this DateTime @this) => @this.Round(TimeSpan.FromSeconds(1));

        public static DateTime RoundToMinute(this DateTime @this) => @this.Round(TimeSpan.FromMinutes(1));

        public static DateTime RoundToHour(this DateTime @this) => @this.Round(TimeSpan.FromHours(1));

        public static DateTime Round(this DateTime @this, TimeSpan nearest)
        {
            if (nearest.Ticks == 0) return @this;
            var ticks = (@this.Ticks + nearest.Ticks / 2) / nearest.Ticks * nearest.Ticks;
            return new DateTime(ticks, @this.Kind);
        }

        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // The date's Kind is ignored, as in Olive.
        public static long ToUnixTime(this DateTime @this) => (long)(@this - new DateTime(1970, 1, 1)).TotalSeconds;

        public static DateTime FromUnixTime(this long @this) => UnixEpoch.AddSeconds(@this).ToLocalTime();

        public static string ToString(this DateTime? @this, string format) => @this?.ToString(format) ?? string.Empty;

        public static string ToSmallTime(this DateTime date) => date.ToString(date.Minute == 0 ? "htt" : "h:mmtt").ToLower();

        // Any future date shows as a weekday name, however far ahead, as in Olive.
        public static string ToFriendlyDateString(this DateTime @this)
        {
            var today = DateTime.Today;
            string day;
            if (@this.Date == today) day = "Today";
            else if (@this.Date == today.AddDays(-1)) day = "Yesterday";
            else if (@this.Date >= today.AddDays(-5)) day = @this.DayOfWeek.ToString();
            else day = @this.ToString("MMMM dd, yyyy");
            return day + " @ " + @this.ToShortTimeString().ToLower();
        }

        public static string ToTimeDifferenceString(this DateTime @this) => @this.ToTimeDifferenceString(2, true);

        public static string ToTimeDifferenceString(this DateTime @this, bool longForm) => @this.ToTimeDifferenceString(2, longForm);

        public static string ToTimeDifferenceString(this DateTime @this, int precisionParts) => @this.ToTimeDifferenceString(precisionParts, true);

        public static string ToTimeDifferenceString(this DateTime @this, int precisionParts, bool longForm)
        {
            var now = DateTime.Now;
            return @this <= now ? (now - @this).ToNaturalTime(precisionParts, longForm) + " ago" : (@this - now).ToNaturalTime(precisionParts, longForm) + " later";
        }

        public static string ToTimeDifferenceString(this DateTime? @this, int precisionParts = 2, bool longForm = true) =>
            @this?.ToTimeDifferenceString(precisionParts, longForm);

        public static string ToNaturalTime(this TimeSpan @this) => @this.ToNaturalTime(2, true);

        public static string ToNaturalTime(this TimeSpan @this, bool longForm) => @this.ToNaturalTime(2, longForm);

        public static string ToNaturalTime(this TimeSpan @this, int precisionParts) => @this.ToNaturalTime(precisionParts, true);

        // Years are 365 days and months 30, as in Olive. Under a second shows the fraction ("0.4 second").
        public static string ToNaturalTime(this TimeSpan @this, int precisionParts, bool longForm)
        {
            if (@this.Ticks <= 0 || precisionParts <= 0) return string.Empty;
            var parts = new List<string>();
            void Add(double value, string longName, string shortName) =>
                parts.Add(longForm ? value + " " + longName + (value > 1 ? "s" : "") : value + shortName);

            var days = @this.Days;
            if (days >= 365) { Add(days / 365, "year", "y"); days %= 365; }
            if (days >= 30) { Add(days / 30, "month", "M"); days %= 30; }
            if (days >= 7) { Add(days / 7, "week", "w"); days %= 7; }
            if (days > 0) Add(days, "day", "d");
            if (@this.Hours > 0) Add(@this.Hours, "hour", "h");
            if (@this.Minutes > 0) Add(@this.Minutes, "minute", "m");
            var subMinute = @this.Ticks % TimeSpan.TicksPerMinute;
            if (@this.Seconds > 0) Add(@this.Seconds, "second", "s");
            else if (subMinute > 0) Add(Math.Round(subMinute / (double)TimeSpan.TicksPerSecond, 3), "second", "s");

            var taken = parts.Take(precisionParts).ToList();
            if (!longForm) return string.Join(" ", taken);
            return taken.Count == 1 ? taken[0] : string.Join(", ", taken.Take(taken.Count - 1)) + " and " + taken.Last();
        }
    }
}
