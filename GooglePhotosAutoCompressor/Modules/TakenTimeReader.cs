using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    /// <summary>
    /// Before an original goes to the bin, reads its date, time and time zone as Google Photos shows them
    /// (this includes edits the user made in Google Photos, which the file itself does not have).
    /// Read-only: the date dialog is opened, read and closed with Escape, never saved.
    /// </summary>
    internal static class TakenTimeReader
    {
        public static async Task ReadTakenTimes(IPage page, FileItemsData files, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("\n===\nReadTakenTimes\n===\n");

            foreach (FileItem item in files.Snapshot().Where(ItemRules.NeedsTakenTime))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    (DateTime? takenAt, int? offsetMs) = await ReadOne(page, item.MediaKey);

                    if (takenAt == null)
                    {
                        Miss(item, "the date dialog had no readable time");
                    }
                    else if (item.Date != default && DateOnly.FromDateTime(takenAt.Value) != item.Date)
                    {
                        // The list showed another date: not the page we meant, or the date was just changed.
                        Miss(item, $"date {takenAt.Value:d MMM yyyy} does not match the list ({item.Date:d MMM yyyy})");
                    }
                    else if (PhotoCompressor.IsSupported(item.CompressPath) && PhotoCompressor.ApplyTakenTime(item, takenAt.Value, offsetMs) == false)
                    {
                        // Photo copies are placed by their EXIF date (there is no date step for them), so it must be right first.
                        Miss(item, "the photo copy's date could not be set to it");
                    }
                    else
                    {
                        item.TakenAt = takenAt;
                        item.TakenOffsetMs = offsetMs;
                        Console.WriteLine($"TakenTimeReader: {item.MediaKey} taken {TakenTime.Describe(takenAt.Value, offsetMs)}");
                    }
                }
                catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException)
                {
                    if (await Network.IsOnlineAsync(cancellationToken) == false)
                    {
                        // Not the item's fault. The step is retried when the internet is back.
                        throw;
                    }

                    Miss(item, e.Message.Split('\n')[0]);
                }

                files.Save();
            }
        }

        internal static async Task<(DateTime? takenAt, int? offsetMs)> ReadOne(IPage page, string mediaKey)
        {
            await page.GotoAsync(Selectors.ViewerUrl(mediaKey));
            if (DateSetter.ParseMediaKeyFromUrl(page.Url) != mediaKey)
            {
                throw new InvalidOperationException("the item did not open in the viewer");
            }

            await DateDialog.ShowInfoPanel(page);
            ILocator dialog = await DateDialog.Open(page);
            try
            {
                return await DateDialog.Read(dialog);
            }
            finally
            {
                await DateDialog.CloseWithoutSaving(page, dialog);
            }
        }

        private static void Miss(FileItem item, string reason)
        {
            item.TakenReadMisses++;
            string next = item.TakenReadMisses >= TakenTime.READ_TRIES
                ? "the copy will keep the time stored in the file"
                : "will try again next run";
            Console.WriteLine($"TakenTimeReader: {item.MediaKey}: could not read the time ({reason}); {next}.");
        }
    }
}
