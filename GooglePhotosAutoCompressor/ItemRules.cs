namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Decides what may happen to an item next. All data-safety rules live here so they can be tested.
    /// </summary>
    internal static class ItemRules
    {
        public static bool IsCompressionReasonable(FileItem item) => IsCompressionReasonable(item, Settings.Current);

        public static bool IsCompressionReasonable(FileItem item, Settings settings)
        {
            if (item.MediaType == MediaType.Image)
            {
                return settings.CompressPhotos && item.Size >= settings.MinPhotoBytes;
            }

            if (settings.CompressVideos == false || item.Duration <= TimeSpan.Zero)
            {
                return false;
            }

            return item.Size / item.Duration.TotalSeconds >= settings.MinVideoBytesPerSecond;
        }

        /// <summary>Gave up on it: not worth compressing, or failed too often.</summary>
        public static bool IsAbandoned(FileItem item)
        {
            return item.CompressionSkipped || item.FailCount >= Settings.Current.MaxFails;
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
        /// The original's date and time must be read from Google Photos before it goes to the bin; afterwards it
        /// is gone. Only when reading keeps failing does the original go anyway (the copy keeps the file's own time).
        /// </summary>
        public static bool NeedsTakenTime(FileItem item)
        {
            return CanDeleteOriginal(item) && item.TakenAt == null && item.TakenReadMisses < TakenTime.READ_TRIES;
        }

        /// <summary>A good copy exists and the original's date and time are known (or could not be read after several tries).</summary>
        public static bool IsReadyForBin(FileItem item)
        {
            return CanDeleteOriginal(item) && NeedsTakenTime(item) == false;
        }

        /// <summary>
        /// Upload only after the original is in the bin (storage may be full),
        /// otherwise the library ends up with both copies.
        /// </summary>
        public static bool CanUpload(FileItem item)
        {
            return item.OriginalDeleted && item.CompressUploaded == false && item.Verified && FileExists(item.CompressPath);
        }

        /// <summary>
        /// Copy is (probably) in Google Photos and still needs its date and time fixed.
        /// Photo copies never need it: their EXIF date taken is the original's time (set before the bin, see
        /// <see cref="PhotoCompressor.ApplyTakenTime"/>), and Google places them by it.
        /// </summary>
        public static bool NeedsDateSet(FileItem item)
        {
            return item.OriginalDeleted && PhotoCompressor.IsSupported(item.CompressPath) == false && item.DateSet == false && (item.CompressUploaded || item.Verified)
                   && string.IsNullOrEmpty(item.CompressPath) == false;
        }

        /// <summary>Finished: copy uploaded and dated (photo copies are dated on upload).</summary>
        public static bool IsDone(FileItem item) => item.DateSet && (item.CompressUploaded || item.OriginalDeleted);

        /// <summary>One status per item for the UI, in pipeline order.</summary>
        public static ItemStatus Describe(FileItem item)
        {
            if (IsDone(item)) return ItemStatus.Done;
            if (item.CompressUploaded) return ItemStatus.Uploaded;
            if (item.OriginalDeleted) return ItemStatus.InBin;
            if (item.FailCount >= Settings.Current.MaxFails) return ItemStatus.Failed;
            if (item.CompressionSkipped) return ItemStatus.Skipped;
            if (item.Verified) return ItemStatus.Compressed;
            if (FileExists(item.DownloadPath)) return ItemStatus.Downloaded;
            if (IsCompressionReasonable(item) == false) return ItemStatus.Small;
            return ItemStatus.Waiting;
        }

        private static bool FileExists(string? path) => string.IsNullOrEmpty(path) == false && File.Exists(path);
    }

    internal enum ItemStatus
    {
        /// <summary>Big enough; not started yet.</summary>
        Waiting,
        /// <summary>Below the size limits (or its type is turned off); left alone.</summary>
        Small,
        Downloaded,
        /// <summary>Copy made and checked.</summary>
        Compressed,
        /// <summary>Original moved to the bin; copy not uploaded yet.</summary>
        InBin,
        /// <summary>Copy uploaded; date not set yet.</summary>
        Uploaded,
        Done,
        /// <summary>Copy would not save space, or the format is not supported.</summary>
        Skipped,
        Failed,
    }
}
