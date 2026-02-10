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
