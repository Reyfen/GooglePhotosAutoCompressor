namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Decides what may happen to an item next. All data-safety rules live here so they can be tested.
    /// </summary>
    internal static class ItemRules
    {
        public static bool IsCompressionReasonable(FileItem item)
        {
            if (item.Duration <= TimeSpan.Zero)
            {
                return false;
            }

            return item.Size / item.Duration.TotalSeconds >= Const.MIN_SIZE_PER_SEC_TO_COMPRESS;
        }

        /// <summary>Gave up on it: not worth compressing, or failed too often.</summary>
        public static bool IsAbandoned(FileItem item)
        {
            return item.CompressionSkipped || item.FailCount >= Const.MAX_FAILS;
        }

        public static bool NeedsDownload(FileItem item)
        {
            if (IsAbandoned(item) || item.OriginalDeleted || item.CompressUploaded || item.DateSet)
            {
                return false;
            }

            return FileExists(item.DownloadPath) == false && FileExists(item.CompressPath) == false;
        }

        /// <summary>Started but not finished, so it counts against the batch limit.</summary>
        public static bool IsInProgress(FileItem item)
        {
            return IsCompressionReasonable(item) && item.DateSet == false && (IsAbandoned(item) == false || item.OriginalDeleted);
        }

        /// <summary>Has a download (or an unchecked copy) that still needs compressing and checking.</summary>
        public static bool NeedsCompression(FileItem item)
        {
            if (item.Verified || item.CompressUploaded || IsAbandoned(item))
            {
                return false;
            }

            return FileExists(item.DownloadPath) || FileExists(item.CompressPath);
        }

        /// <summary>
        /// The original may go to the bin only when a good copy exists:
        /// either the copy is already uploaded, or it is verified and still on disk.
        /// </summary>
        public static bool CanDeleteOriginal(FileItem item)
        {
            if (item.OriginalDeleted)
            {
                return false;
            }

            return item.CompressUploaded || (item.Verified && FileExists(item.CompressPath));
        }

        /// <summary>
        /// Upload only after the original is in the bin (storage may be full),
        /// otherwise the library ends up with both copies.
        /// </summary>
        public static bool CanUpload(FileItem item)
        {
            return item.OriginalDeleted && item.CompressUploaded == false && item.Verified && FileExists(item.CompressPath);
        }

        private static bool FileExists(string? path) => string.IsNullOrEmpty(path) == false && File.Exists(path);
    }
}
