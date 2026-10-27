using System.Globalization;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Turns the text of a tile on the "large files" page into a <see cref="FileItem"/>.
    /// Pure logic, no browser, so it can be unit tested.
    /// </summary>
    internal static class TileParser
    {
        public static FileItem? Parse(string? mediaKey, IEnumerable<string> innerTexts)
        {
            if (string.IsNullOrEmpty(mediaKey))
            {
                return null;
            }

            string[] texts = string.Join('\n', innerTexts).ToLowerInvariant().Split('\n').Select(t => t.Trim()).ToArray();

            int sizeIndex = Array.FindIndex(texts, t => t.EndsWith("kb") || t.EndsWith("mb") || t.EndsWith("gb"));
            if (sizeIndex < 0)
            {
                return null;
            }

            long size = ParseSizeToBytes(texts[sizeIndex]);
            if (size <= 0)
            {
                return null;
            }

            texts[sizeIndex] = string.Empty;

            DateOnly date = default;
            // en-GB writes September as "Sept"; the invariant culture only knows "Sep".
            texts = texts.Select(t => t.Replace(" sept ", " sep ")).ToArray();
            int dateIndex = Array.FindIndex(texts, t => DateOnly.TryParseExact(t, Const.DATE_FORMATS, CultureInfo.InvariantCulture, DateTimeStyles.None, out date));
            if (dateIndex < 0)
            {
                return null;
            }

            texts[dateIndex] = string.Empty;

            // Videos show a duration; photos do not.
            TimeSpan duration = default;
            int durationIndex = Array.FindIndex(texts, t => TimeSpan.TryParseExact(t, Const.TIME_FORMATS, CultureInfo.InvariantCulture, out duration));
            if (durationIndex < 0)
            {
                return new() { MediaType = MediaType.Image, MediaKey = mediaKey, Date = date, Size = size };
            }

            if (duration <= TimeSpan.Zero)
            {
                return null;
            }

            return new() { MediaType = MediaType.Video, MediaKey = mediaKey, Date = date, Duration = duration, Size = size };
        }

        /// <summary>Parses "12.5 MB" style text. Returns -1 when the text is not a size.</summary>
        public static long ParseSizeToBytes(string input)
        {
            string[] parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                return -1;
            }

            if (double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value) == false)
            {
                return -1;
            }

            return parts[1].ToUpperInvariant() switch
            {
                "B" => (long)value,
                "KB" => (long)(value * 1024),
                "MB" => (long)(value * 1024 * 1024),
                "GB" => (long)(value * 1024 * 1024 * 1024),
                _ => -1,
            };
        }
    }
}
