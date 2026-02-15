namespace GooglePhotosAutoCompressor
{
    internal class FileItem : IEquatable<FileItem>
    {

        public MediaType MediaType { get; }
        public string MediaKey { get; }
        public DateOnly Date { get; }
        public TimeSpan Duration { get; }
        public long Size { get; }

        public string DownloadPath { get; set; }
        public string CompressPath { get; set; }
        public bool OriginalDeleted { get; set; }
        public bool CompressUploaded { get; set; }
        public bool DateSet { get; set; }

        public FileItem(MediaType mediaType, string mediaKey, DateOnly date, TimeSpan duraion, long size)
        {
            MediaType = mediaType;
            MediaKey = mediaKey;
            Date = date;
            Duration = duraion;
            Size = size;
        }

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
