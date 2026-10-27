namespace GooglePhotosAutoCompressor
{
    internal class FileItem : IEquatable<FileItem>
    {
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
        /// <summary>Failed attempts; the item is left alone after <see cref="Const.MAX_FAILS"/>.</summary>
        public int FailCount { get; set; }
        public bool OriginalDeleted { get; set; }
        public bool CompressUploaded { get; set; }
        public bool DateSet { get; set; }

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
}
