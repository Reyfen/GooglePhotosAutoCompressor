using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class DateSetter
    {
        /// <summary>After this many runs without finding an "uploaded" copy, upload it again.</summary>
        internal const int MISSES_BEFORE_REUPLOAD = 3;

        internal static async Task SetCompressedFilesDates(IPage page, FileItemsData files, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("\n===\nSetCompressedFilesDates\n===\n");

            // File name shown in Google Photos -> item. Names are unique (they contain a media key id).
            Dictionary<string, FileItem> pending = files.Snapshot()
                .Where(ItemRules.NeedsDateSet)
                .ToDictionary(i => Path.GetFileName(i.CompressPath));

            if (pending.Count == 0)
            {
                return;
            }

            // "Recently added" lists items by upload time, newest first. The library itself sorts by date taken,
            // and copies that keep their creation time land at their original date there (seen live 2026-10-29).
            await page.GotoAsync(Selectors.RECENTLY_ADDED_URL);
            await page.Locator(Selectors.LIBRARY_FIRST_ITEM).First.ClickAsync();

            ILocator fileNamePanels = page.Locator(Selectors.FILE_NAME_PANEL);

            if (await WaitForAnyVisibleAsync(fileNamePanels, 3000) == false)
            {
                await page.Locator(Selectors.OPEN_INFO_BUTTON).ClickAsync();
                await WaitForAnyVisibleAsync(fileNamePanels, 3000);
            }

            ILocator nextPhotoButton = page.Locator(Selectors.NEXT_PHOTO_BUTTON);

            // New uploads sit at the top of "Recently added". Give up after this many other items in a row
            // (photos synced from a phone during the run also land there, so allow some slack).
            int maxMissesInRow = 20 + pending.Count;
            int missesInRow = 0;

            for (int i = 0; i < 10_000 && pending.Count > 0 && missesInRow < maxMissesInRow && cancellationToken.IsCancellationRequested == false; i++)
            {
                try
                {
                    string fileName = (await (await FindActivePanel(fileNamePanels)).InnerTextAsync()).Trim();

                    if (pending.Remove(fileName, out FileItem? file))
                    {
                        // Found it, so the upload certainly worked. Saved before the date, in case setting it fails.
                        file.CompressUploaded = true;
                        file.UploadedMediaKey = ParseMediaKeyFromUrl(page.Url);
                        files.Save();
                        missesInRow = 0;

                        ILocator dialog = await DateDialog.Open(page);
                        (DateTime? shown, int? shownOffset) = await DateDialog.Read(dialog);
                        bool alreadyRight = TakenTime.AlreadyShown(shown, shownOffset, file.Date, file.TakenAt, file.TakenOffsetMs);
                        if (alreadyRight)
                        {
                            // Usual case: Google read the creation time kept in the file. Nothing to save.
                            await DateDialog.CloseWithoutSaving(page, dialog);
                        }
                        else
                        {
                            await DateDialog.Save(page, dialog, file.Date, file.TakenAt, file.TakenOffsetMs);
                        }

                        file.DateSet = true;
                        file.DateSetMisses = 0;
                        files.Save();

                        string wanted = file.TakenAt is DateTime t ? TakenTime.Describe(t, file.TakenOffsetMs) : $"{file.Date:d MMM yyyy} (time kept from the file)";
                        Console.WriteLine($"DateSetter: {fileName}: {(alreadyRight ? "already" : "set to")} {wanted}");
                    }
                    else
                    {
                        missesInRow++;
                    }
                }
                catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException)
                {
                    missesInRow++;
                    Console.WriteLine($"DateSetter: skipped an item: {e.Message}");
                }

                await nextPhotoButton.First.ClickAsync();
                await Task.Delay(1000);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach ((string name, FileItem file) in pending)
            {
                Console.WriteLine($"DateSetter: not found this run: {name}");
                CountMiss(file);
            }

            files.Save();
        }

        /// <summary>
        /// An upload can look finished and still fail (connection lost, app closed mid-upload). When the copy
        /// is not found again and again, upload it again; Google Photos ignores an identical re-upload.
        /// </summary>
        internal static void CountMiss(FileItem file)
        {
            file.DateSetMisses++;
            if (file.DateSetMisses >= MISSES_BEFORE_REUPLOAD && file.CompressUploaded && File.Exists(file.CompressPath))
            {
                file.CompressUploaded = false;
                file.DateSetMisses = 0;
                Console.WriteLine($"DateSetter: {Path.GetFileName(file.CompressPath)} not found {MISSES_BEFORE_REUPLOAD} times, will upload it again.");
            }
        }

        /// <summary>Viewer URLs look like https://photos.google.com/photo/AF1Qip...?hl=en-GB</summary>
        internal static string? ParseMediaKeyFromUrl(string url)
        {
            Match match = Regex.Match(url, @"/photo/([A-Za-z0-9_-]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        internal static async Task<ILocator> FindActivePanel(ILocator panels)
        {
            int count = await panels.CountAsync();
            for (int i = 0; i < count; i++)
            {
                ILocator panel = panels.Nth(i);
                LocatorBoundingBoxResult? box = await panel.BoundingBoxAsync();

                if (box == null || box.Width <= 0)
                {
                    continue;
                }

                return panel;
            }

            throw new InvalidOperationException("panel not found");
        }

        public static async Task<bool> WaitForAnyVisibleAsync(ILocator locator, int timeout)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);

            while (DateTime.UtcNow < deadline)
            {
                var count = await locator.CountAsync();
                for (var i = 0; i < count; i++)
                {
                    if (await locator.Nth(i).IsVisibleAsync())
                    {
                        return true;
                    }
                }

                await Task.Delay(100);
            }

            return false;
        }
    }
}
