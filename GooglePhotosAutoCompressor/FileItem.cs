namespace GooglePhotosAutoCompressor
{
    internal class FileItem : IEquatable<FileItem>
    {
        /// <summary>Video unless the tile had no duration. Missing in old state files, which only held videos.</summary>
        public MediaType MediaType { get; init; } = MediaType.Video;
        public string MediaKey { get; init; }
        public DateOnly Date { get; init; }
        public TimeSpan Duration { get; init; }
        public long Size { get; init; }

        public string DownloadPath { get; set; }
        public string CompressPath { get; set; }
        /// <summary>Compressed file was checked (duration matches, smaller than the original).</summary>
        public bool Verified { get; set; }
        /// <summary>Compression would not save space (or the format is unsupported); leave the original alone.</summary>
        public bool CompressionSkipped { get; set; }
        /// <summary>Failed attempts; the item is left alone after <see cref="Settings.MaxFails"/>.</summary>
        public int FailCount { get; set; }
        public bool OriginalDeleted { get; set; }
        public bool CompressUploaded { get; set; }
        public bool DateSet { get; set; }
        /// <summary>Size of the checked copy, kept after the local file is gone (for "space saved").</summary>
        public long CompressedSize { get; set; }
        /// <summary>Runs in which the date step looked for the uploaded copy and did not find it.</summary>
        public int DateSetMisses { get; set; }
        /// <summary>
        /// Date and time taken of the original as Google Photos shows it (local wall-clock time, to the minute),
        /// read before the original goes to the bin. The copy gets the same date, time and time zone.
        /// </summary>
        public DateTime? TakenAt { get; set; }
        /// <summary>Time zone of <see cref="TakenAt"/> as a UTC offset in milliseconds (Google's own unit).</summary>
        public int? TakenOffsetMs { get; set; }
        /// <summary>Runs in which reading <see cref="TakenAt"/> failed. After a few, the original may go to the bin without it.</summary>
        public int TakenReadMisses { get; set; }
        /// <summary>Media key of our uploaded copy, so later runs never treat it as a new original.</summary>
        public string? UploadedMediaKey { get; set; }

        public bool Equals(FileItem? other)
        {
            return other is not null && other.MediaKey == MediaKey;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as FileItem);
        }

        public override int GetHashCode()
        {
            return MediaKey.GetHashCode();
        }

        public static bool operator ==(FileItem a, FileItem b)
        {
            if (a is null)
            {
                return b is null;
            }

            return a.Equals(b);
        }

        public static bool operator !=(FileItem a, FileItem b)
        {
            return (a == b) == false;
        }
    }

    internal enum MediaType
    {
        Video,
        Image,
    }
}
