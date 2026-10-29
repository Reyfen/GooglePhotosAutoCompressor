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
        /// <summary>Everything the app keeps in Downloads (progress, originals, copies) goes in this one folder.</summary>
        public static readonly string APP_DOWNLOADS_PATH = Path.Combine(DOWNLOADS_PATH, "GooglePhotosAutoCompressor");
    }
}
