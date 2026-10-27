namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Every Google Photos selector in one place. The UI is forced to en-GB (<see cref="Const.LOCALE"/>),
    /// so aria-labels are stable. When Google changes the page, this is the file to fix.
    /// </summary>
    internal static class Selectors
    {
        // Large files page (photos.google.com/quotamanagement/large)
        public const string LARGE_FILE_TILE = "[data-media-key]:has(div[role='checkbox'])";
        public const string LARGE_FILES_SCROLL_CHILD = "[data-p*='mediaKey']";
        public const string DOWNLOAD_BUTTON = "[aria-label='Download']";
        public const string MOVE_TO_BIN_BUTTON = "[aria-label='Move to bin']";
        public const string DIALOG_DEFAULT_BUTTON = "[data-mdc-dialog-button-default]";

        // Upload
        public const string CREATE_BUTTON = "[aria-label='Create and add photos']";
        public const string IMPORT_FROM_COMPUTER = "[aria-label='Import photos from your computer']";
        public const string UPLOAD_PROGRESS = "[aria-live='polite'] [data-progressvalue]";

        // Photo viewer and info panel
        public const string LIBRARY_FIRST_ITEM = "[data-latest-bg]";
        public const string OPEN_INFO_BUTTON = "button[aria-label='Open info']";
        public const string NEXT_PHOTO_BUTTON = "[aria-label='View next photo']";
        public const string FILE_NAME_PANEL = "[aria-label^='Filename:']";
        public const string DATE_TAKEN_PANEL = "[aria-label^='Date taken:']";
        public const string DIALOG = "[role='dialog']";
        public const string DATE_YEAR_INPUT = "input[aria-label='Year']";
        public const string DATE_MONTH_INPUT = "input[aria-label='Month']";
        public const string DATE_DAY_INPUT = "input[aria-label='Day']";

        public static string TileByMediaKey(string mediaKey) => $"[data-media-key='{mediaKey}']";
    }
}
