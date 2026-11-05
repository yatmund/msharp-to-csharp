using System;
using System.Globalization;
using System.Text;

namespace Platform
{
    public static partial class Extensions
    {
        // Through decimal, as in Olive: 15 significant digits, and no negative zero.
        public static double Round(this double @this, int digits) => (double)Math.Round((decimal)@this, digits, MidpointRounding.AwayFromZero);

        public static decimal Round(this decimal @this, int digits) => Math.Round(@this, digits, MidpointRounding.AwayFromZero);

        public static float Round(this float @this, int digits) => (float)Math.Round((decimal)@this, digits, MidpointRounding.AwayFromZero);

        public static double Truncate(this double @this, int places)
        {
            var factor = Math.Pow(10, places);
            return Math.Truncate(@this * factor) / factor;
        }

        public static string ToString(this int? @this, string format) => @this?.ToString(format) ?? string.Empty;

        public static string ToString(this double? @this, string format) => @this?.ToString(format) ?? string.Empty;

        public static string ToString(this decimal? @this, string format) => @this?.ToString(format) ?? string.Empty;

        public static double AsPercentageOf(this double @this, double total, bool multiplyBy100 = true, int? roundTo = null)
        {
            if (total == 0) return 0;
            var result = @this / total;
            if (multiplyBy100) result *= 100;
            return roundTo.HasValue ? result.Round(roundTo.Value) : result;
        }

        // Banker's rounding here, unlike Round(), as in Olive.
        public static decimal AsPercentageOf(this decimal @this, decimal total, bool multiplyBy100 = true, int? roundTo = null)
        {
            if (total == 0) return 0;
            var result = @this / total;
            if (multiplyBy100) result *= 100;
            return roundTo.HasValue ? Math.Round(result, roundTo.Value) : result;
        }

        public static double AsPercentageOf(this int @this, int total, bool multiplyBy100 = true, int? roundTo = 0) =>
            ((double)@this).AsPercentageOf(total, multiplyBy100, roundTo);

        public static double RoundUpToNearest(this double @this, double roundIntervals) => roundIntervals == 0 ? @this : Math.Ceiling(@this / roundIntervals) * roundIntervals;

        public static decimal RoundUpToNearest(this decimal @this, decimal roundIntervals) => roundIntervals == 0 ? @this : Math.Ceiling(@this / roundIntervals) * roundIntervals;

        public static double RoundDownToNearest(this double @this, double roundIntervals) => roundIntervals == 0 ? @this : Math.Floor(@this / roundIntervals) * roundIntervals;

        public static decimal RoundDownToNearest(this decimal @this, decimal roundIntervals) => roundIntervals == 0 ? @this : Math.Floor(@this / roundIntervals) * roundIntervals;

        public static int RoundUpToNearest(this int value, int roundIntervals) => roundIntervals == 0 ? value : (int)Math.Ceiling((double)value / roundIntervals) * roundIntervals;

        public static int RoundDownToNearest(this int value, int roundIntervals) => roundIntervals == 0 ? value : (int)Math.Floor((double)value / roundIntervals) * roundIntervals;

        public static bool AlmostEquals(this double @this, double otherValue, double tolerance = 1E-05) => Math.Abs(@this - otherValue) <= tolerance;

        public static bool AlmostEquals(this float @this, float otherValue, float tolerance = 0.001f) => Math.Abs(@this - otherValue) <= tolerance;

        public static float LimitMax(this float @this, float maxValue) => Math.Min(@this, maxValue);

        public static float LimitMin(this float @this, float minValue) => Math.Max(@this, minValue);

        public static float LimitWithin(this float @this, float minValue, float maxValue) => @this.LimitMin(minValue).LimitMax(maxValue);

        public static double LimitMax(this double @this, double maxValue) => Math.Min(@this, maxValue);

        public static double LimitMin(this double @this, double minValue) => Math.Max(@this, minValue);

        public static double LimitWithin(this double @this, double minValue, double maxValue) => @this.LimitMin(minValue).LimitMax(maxValue);

        public static int LimitMax(this int @this, int maxValue) => Math.Min(@this, maxValue);

        public static int LimitMin(this int @this, int minValue) => Math.Max(@this, minValue);

        public static int LimitWithin(this int @this, int minValue, int maxValue) => @this.LimitMin(minValue).LimitMax(maxValue);

        public static long LimitMax(this long @this, long maxValue) => Math.Min(@this, maxValue);

        public static long LimitMin(this long @this, long minValue) => Math.Max(@this, minValue);

        public static long LimitWithin(this long @this, long minValue, long maxValue) => @this.LimitMin(minValue).LimitMax(maxValue);

        public static decimal LimitMax(this decimal @this, decimal maxValue) => Math.Min(@this, maxValue);

        public static decimal LimitMin(this decimal @this, decimal minValue) => Math.Max(@this, minValue);

        public static decimal LimitWithin(this decimal @this, decimal minValue, decimal maxValue) => @this.LimitMin(minValue).LimitMax(maxValue);

        public static string ToOrdinal(this int @this)
        {
            var lastTwo = @this % 100;
            var suffix = lastTwo >= 11 && lastTwo <= 13 ? "th" : (@this % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return @this + suffix;
        }

        static readonly string[] Units = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
            "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };

        static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        // Olive leaves a trailing space after "hundred", "thousand" and "million" with nothing after them; kept for identical output.
        public static string ToWordString(this int @this)
        {
            if (@this == 0) return "zero";
            if (@this < 0) return "minus " + (-@this).ToWordString();
            var words = new StringBuilder();
            void Group(int size, string name)
            {
                if (@this / size == 0) return;
                words.Append((@this / size).ToWordString()).Append(' ').Append(name).Append(' ');
                @this %= size;
            }
            Group(1000000000, "billion");
            Group(1000000, "million");
            Group(1000, "thousand");
            Group(100, "hundred");
            if (@this > 0)
            {
                if (words.Length > 0) words.Append("and ");
                words.Append(@this < 20 ? Units[@this] : Tens[@this / 10] + (@this % 10 > 0 ? "-" + Units[@this % 10] : ""));
            }
            return words.ToString();
        }

        public static string ToFileSizeString(this int @this, int round = 1) => ((long)@this).ToFileSizeString(round);

        public static string ToFileSizeString(this int @this, string units, int round) => ((long)@this).ToFileSizeString(units, round);

        public static string ToFileSizeString(this long @this, int round = 1)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = @this;
            var unit = 0;
            while (size >= 1024) { size /= 1024; unit++; }
            return (unit == 0 ? size : Math.Round(size, round)) + units[unit];
        }

        // Only "MB" is honoured, as in Olive; anything else picks B, KB, MB or GB with integer division.
        public static string ToFileSizeString(this long fileSize, string units, int round)
        {
            if (string.Equals(units, "MB", StringComparison.OrdinalIgnoreCase))
                return Math.Round(fileSize / 1048576d, round).ToString("0.0") + " MB";
            if (fileSize <= 1024) return fileSize + " B";
            if (fileSize <= 1048576) return fileSize / 1024 + " KB";
            if (fileSize <= 1073741824) return fileSize / 1048576 + " MB";
            return fileSize / 1073741824 + " GB";
        }

        public static string ToShortMoneyString(this double @this) => @this.ToString("c", CultureInfo.CurrentCulture).Replace(".00", "");

        public static string ToShortMoneyString(this double? @this) => @this?.ToShortMoneyString() ?? string.Empty;

        public static Guid ToGuid(this int @this)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(@this).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        public static TimeSpan Days(this int number) => TimeSpan.FromDays(number);

        public static TimeSpan Hours(this int number) => TimeSpan.FromHours(number);

        public static TimeSpan Minutes(this int number) => TimeSpan.FromMinutes(number);

        public static TimeSpan Seconds(this int number) => TimeSpan.FromSeconds(number);

        public static TimeSpan Milliseconds(this int number) => TimeSpan.FromMilliseconds(number);

        public static double ApproxTotalYears(this TimeSpan @this) => @this.TotalDays / 365.2425;

        public static string ToString(this TimeSpan? @this, string format) => @this?.ToString(format) ?? string.Empty;

        public static string ToString(this bool value, string trueText, string falseText) => value ? trueText : falseText;

        public static string ToString(this bool? @this, string trueText, string falseText, string nullText = null) =>
            @this == null ? nullText : @this.Value ? trueText : falseText;

        public static string ToYesNoString(this bool value, string yes = "Yes", string no = "No") => value ? yes : no;

        public static string ToYesNoString(this bool? value, string yes = "Yes", string no = "No") => value == null ? string.Empty : value.Value ? yes : no;
    }
}
