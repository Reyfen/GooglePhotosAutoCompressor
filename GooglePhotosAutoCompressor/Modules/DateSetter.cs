using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class DateSetter
    {
        internal static async Task SetCompressedFilesDates(IPage page, FileItemsData files)
        {
            Console.WriteLine("\n===\nSetCompressedFilesDates\n===\n");

            // File name shown in Google Photos -> item. Names are unique (they contain a media key id).
            Dictionary<string, FileItem> pending = files.Items
                .Where(ItemRules.NeedsDateSet)
                .ToDictionary(i => Path.GetFileName(i.CompressPath));

            if (pending.Count == 0)
            {
                return;
            }

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");
            await page.Locator(Selectors.LIBRARY_FIRST_ITEM).First.ClickAsync();

            ILocator fileNamePanels = page.Locator(Selectors.FILE_NAME_PANEL);

            if (await WaitForAnyVisibleAsync(fileNamePanels, 3000) == false)
            {
                await page.Locator(Selectors.OPEN_INFO_BUTTON).ClickAsync();
                await WaitForAnyVisibleAsync(fileNamePanels, 3000);
            }

            ILocator nextPhotoButton = page.Locator(Selectors.NEXT_PHOTO_BUTTON);

            // New uploads sit at the top of the library. Give up after this many other items in a row
            // (photos synced from a phone during the run also land there, so allow some slack).
            int maxMissesInRow = 20 + pending.Count;
            int missesInRow = 0;

            for (int i = 0; i < 10_000 && pending.Count > 0 && missesInRow < maxMissesInRow; i++)
            {
                try
                {
                    string fileName = (await (await FindActivePanel(fileNamePanels)).InnerTextAsync()).Trim();

                    if (pending.Remove(fileName, out FileItem? file))
                    {
                        await SetDate(page, file.Date);

                        file.DateSet = true;
                        file.CompressUploaded = true; // found it, so the upload certainly worked
                        file.UploadedMediaKey = ParseMediaKeyFromUrl(page.Url);
                        files.Save();
                        missesInRow = 0;

                        Console.WriteLine($"DateSetter: {fileName} -> {file.Date}");
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

            foreach (string name in pending.Keys)
            {
                Console.WriteLine($"DateSetter: not found this run: {name}");
            }
        }

        private static async Task SetDate(IPage page, DateOnly date)
        {
            ILocator datePanel = await FindActivePanel(page.Locator(Selectors.DATE_TAKEN_PANEL));
            await datePanel.ClickAsync();

            ILocator dateDialog = page.Locator(Selectors.DIALOG);
            await dateDialog.WaitForAsync();

            await dateDialog.Locator(Selectors.DATE_YEAR_INPUT).First.FillAsync(date.Year.ToString());
            await dateDialog.Locator(Selectors.DATE_MONTH_INPUT).First.FillAsync(date.Month.ToString());
            await dateDialog.Locator(Selectors.DATE_DAY_INPUT).First.FillAsync(date.Day.ToString());
            await dateDialog.Locator(Selectors.DIALOG_DEFAULT_BUTTON).First.ClickAsync();
        }

        /// <summary>Viewer URLs look like https://photos.google.com/photo/AF1Qip...?hl=en-GB</summary>
        internal static string? ParseMediaKeyFromUrl(string url)
        {
            Match match = Regex.Match(url, @"/photo/([A-Za-z0-9_-]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static async Task<ILocator> FindActivePanel(ILocator panels)
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
