using System.Globalization;
using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Reading and comparing "date taken" values: the fields of Google's "Edit date and time" dialog,
    /// its time zone label ("GMT+01:00") and the EXIF date of photos ("2025:07:04 17:57:12").
    /// </summary>
    internal static class TakenTime
    {
        /// <summary>Runs in which reading the original's time may fail before the original may go to the bin without it.</summary>
        public const int READ_TRIES = 3;

        /// <summary>The dialog's Year, Month, Day, Hour and Minutes fields. Null when any is missing or invalid.</summary>
        public static DateTime? FromFields(string? year, string? month, string? day, string? hour, string? minutes)
        {
            if (TryInt(year, out int y) && TryInt(month, out int mo) && TryInt(day, out int d) && TryInt(hour, out int h) && TryInt(minutes, out int mi)
                && y is >= 1800 and <= 9999 && mo is >= 1 and <= 12 && h is >= 0 and <= 23 && mi is >= 0 and <= 59
                && d >= 1 && d <= DateTime.DaysInMonth(y, mo))
            {
                return new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Unspecified);
            }

            return null;
        }

        /// <summary>"GMT+01:00", "GMT-03:30" or "GMT" (anywhere in the text) as milliseconds, Google's unit for the time zone list.</summary>
        public static int? ParseOffsetMs(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            Match match = Regex.Match(text, @"GMT(?:([+\-−])(\d{1,2}):(\d{2}))?(?![+\-−\d])");
            if (match.Success == false)
            {
                return null;
            }

            if (match.Groups[1].Success == false)
            {
                return 0;
            }

            int sign = match.Groups[1].Value == "+" ? 1 : -1;
            int ms = (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)) * 60_000;
            return sign * ms;
        }

        public static string FormatOffset(int offsetMs)
        {
            TimeSpan offset = TimeSpan.FromMilliseconds(offsetMs);
            return $"GMT{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
        }

        public static string ToExif(DateTime value) => value.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>EXIF OffsetTimeOriginal, e.g. "+01:00".</summary>
        public static string ToExifOffset(int offsetMs) => FormatOffset(offsetMs)[3..];

        /// <summary>EXIF "yyyy:MM:dd HH:mm:ss".</summary>
        public static DateTime? ParseExif(string? exif)
        {
            if (exif != null && DateTime.TryParseExact(exif.Trim().TrimEnd('\0'), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// True when an uploaded photo copy will show the original's date and time without help: Google places
        /// photos by their EXIF date, so it must match what Google showed for the original (to the minute).
        /// Unknown original time: the copy's EXIF is the original's (checked when compressing), so trust it.
        /// </summary>
        public static bool PhotoKeepsTime(DateTime? originalTakenAt, string? copyExif)
        {
            if (originalTakenAt == null)
            {
                return true;
            }

            DateTime? exif = ParseExif(copyExif);
            return exif != null && TruncateToMinute(exif.Value) == TruncateToMinute(originalTakenAt.Value);
        }

        public static string Describe(DateTime takenAt, int? offsetMs) =>
            takenAt.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + (offsetMs is int ms ? " " + FormatOffset(ms) : "");

        private static DateTime TruncateToMinute(DateTime value) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);

        private static bool TryInt(string? text, out int value) =>
            int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
