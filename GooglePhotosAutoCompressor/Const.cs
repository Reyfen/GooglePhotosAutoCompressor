namespace GooglePhotosAutoCompressor
{
    internal class Const
    {
        public const string LOCALE = "en-GB";
        public static readonly string[] TIME_FORMATS = new[]
        {
            "m\\:ss",
            "mm\\:ss",
            "h\\:mm\\:ss",
            "hh\\:mm\\:ss",
        };
        public static readonly string[] DATE_FORMATS = new[]
        {
            "d MMM yyyy",
            "dd MMM yyyy",
        };
        public static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        public const int TOTAL_FILES_TO_PROCESS_IN_ONE_BATCH = 10;
        public const double MIN_SIZE_PER_SEC_TO_COMPRESS = 0.8 * 1024 * 1024;
        /// <summary>Smaller photos save too little to be worth replacing.</summary>
        public const long MIN_PHOTO_SIZE_TO_COMPRESS = 2 * 1024 * 1024;
        /// <summary>16 MP, the size Google's own "Storage saver" quality keeps.</summary>
        public const long MAX_PHOTO_PIXELS = 16_000_000;
        public const int PHOTO_JPEG_QUALITY = 85;
        /// <summary>A compressed copy larger than this share of the original is not worth replacing it.</summary>
        public const double MAX_COMPRESSED_SIZE_RATIO = 0.9;
        /// <summary>After this many failed attempts an item is left alone.</summary>
        public const int MAX_FAILS = 3;
    }
}
