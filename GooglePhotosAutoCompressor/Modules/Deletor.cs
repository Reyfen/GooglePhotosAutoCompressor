using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Deletor
    {
        internal static async Task DeleteCompressedFiles(IPage page, FileItemsData files)
        {
            Console.WriteLine("\n===\nDeleteCompressedFiles\n===\n");

            // Look for exactly these items. The old version stopped after 21 unknown tiles,
            // which skipped originals further down the list (and left duplicates after upload).
            Dictionary<string, FileItem> pending = files.Items.Where(ItemRules.CanDeleteOriginal).ToDictionary(i => i.MediaKey);

            if (pending.Count == 0)
            {
                return;
            }

            List<FileItem> selected = new();
            using CancellationTokenSource cts = new();
            await LargeFileLocator.EnumearteFiles(page, SelectTile, cts.Token);

            foreach (FileItem missing in pending.Values)
            {
                Console.WriteLine($"Deletor: original not found in large files list: {missing.MediaKey} ({missing.Date})");
            }

            if (selected.Count == 0)
            {
                return;
            }

            ILocator deleteButton = await GetDeleteButton(page);
            await deleteButton.ClickAsync();

            ILocator confirmButton = page.Locator("[data-mdc-dialog-button-default]");
            await confirmButton.ClickAsync();
            await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden });

            foreach (FileItem item in selected)
            {
                // A tile still on the page after the dialog closed was not moved to the bin.
                if (await page.Locator($"[data-media-key='{item.MediaKey}']").CountAsync() > 0)
                {
                    Console.WriteLine($"Deletor: still present after delete, will retry next run: {item.MediaKey}");
                    continue;
                }

                item.OriginalDeleted = true;
                Console.WriteLine($"Deletor: moved to bin: {item.MediaKey} ({item.Date})");
            }

            files.Save();

            async Task SelectTile(ILocator tile, FileItem item)
            {
                if (pending.Remove(item.MediaKey, out FileItem? stored) == false)
                {
                    return;
                }

                await tile.ClickAsync();
                await Task.Delay(100);
                selected.Add(stored);

                if (pending.Count == 0)
                {
                    cts.Cancel();
                }
            }
        }

        private static async Task<ILocator> GetDeleteButton(IPage page)
        {
            ILocator deleteButtons = page.Locator("[aria-label='Move to bin']");
            int count = await deleteButtons.CountAsync();

            for (int i = 0; i < count; i++)
            {
                var button = deleteButtons.Nth(i);
                var box = await button.BoundingBoxAsync();

                if (box is null || box.Width <= 0)
                {
                    continue;
                }

                return button;
            }

            throw new Exception("delete button not found");
        }
    }
}
