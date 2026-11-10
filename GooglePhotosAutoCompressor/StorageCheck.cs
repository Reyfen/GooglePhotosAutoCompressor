using System.Globalization;
using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Google Photos storage full: uploads fail, while the originals already wait in the bin (deleted after 60 days).
    /// Two read-only signals from the page text: the storage meter ("12.4 GB of 15 GB used") before uploading, and
    /// Google's out-of-storage wording after an upload failed. Either stops the run (<see cref="StorageFullException"/>),
    /// so no more originals go to the bin until there is room.
    /// </summary>
    internal static class StorageCheck
    {
        private static readonly Regex METER = new(@"([\d.,]+)\s*(KB|MB|GB|TB)\s+of\s+([\d.,]+)\s*(KB|MB|GB|TB)\s+used", RegexOptions.IgnoreCase);

        // Google's wording varies; these are the phrases its storage-full notices use.
        private static readonly Regex FULL = new(
            @"out of (account )?storage|storage (is )?full|not enough (account )?storage|account storage is full|you've run out of storage|no (more )?storage (left|space)",
            RegexOptions.IgnoreCase);

        /// <summary>Used and total bytes from the storage meter text, or null when the page doesn't show it.</summary>
        public static (double used, double total)? ParseMeter(string? pageText)
        {
            if (string.IsNullOrEmpty(pageText))
            {
                return null;
            }

            Match m = METER.Match(pageText);
            if (m.Success == false)
            {
                return null;
            }

            double? used = Bytes(m.Groups[1].Value, m.Groups[2].Value), total = Bytes(m.Groups[3].Value, m.Groups[4].Value);
            return used != null && total is > 0 ? (used.Value, total.Value) : null;
        }

        /// <summary>
        /// Why the next upload of <paramref name="bytes"/> won't fit, or null. Unknown meter: null (the upload itself
        /// will tell). The meter is rounded to one decimal, so this only stops when it is clearly full.
        /// </summary>
        public static string? WontFit(string? pageText, long bytes)
        {
            if (ParseMeter(pageText) is not (double used, double total))
            {
                return null;
            }

            double free = total - used;
            if (free >= bytes)
            {
                return null;
            }

            return $"Google Photos storage is full ({used / 1e9:0.##} GB of {total / 1e9:0.##} GB used), so the copies can't be uploaded.";
        }

        public static bool SaysFull(string? pageText) => string.IsNullOrEmpty(pageText) == false && FULL.IsMatch(pageText);

        private static double? Bytes(string number, string unit)
        {
            if (double.TryParse(number.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) == false)
            {
                return null;
            }

            // Google's storage figures are decimal (1 GB = 1,000,000,000 bytes).
            return value * unit.ToUpperInvariant() switch { "KB" => 1e3, "MB" => 1e6, "GB" => 1e9, _ => 1e12 };
        }
    }

    /// <summary>Stops the run: nothing more may go to the bin while copies can't be uploaded.</summary>
    internal sealed class StorageFullException : Exception
    {
        public StorageFullException(string reason)
            : base(reason + " The run stopped so no more originals go to the bin. Free some space (or empty the bin) " +
                   "and press Start again: the waiting copies are uploaded then. Google deletes bin items after 60 days.")
        {
        }
    }
}
