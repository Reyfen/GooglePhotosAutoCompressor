using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Deletor
    {
        internal static async Task DeleteCompressedFiles(IPage page, FileItemsData files, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("\n===\nDeleteCompressedFiles\n===\n");

            // Look for exactly these items. The old version stopped after 21 unknown tiles,
            // which skipped originals further down the list (and left duplicates after upload).
            Dictionary<string, FileItem> pending = files.Snapshot().Where(ItemRules.CanDeleteOriginal).ToDictionary(i => i.MediaKey);

            if (pending.Count == 0)
            {
                return;
            }

            List<FileItem> selected = new();
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bool sawWholeList = await LargeFileLocator.EnumearteFiles(page, SelectTile, cts.Token);

            if (cancellationToken.IsCancellationRequested)
            {
                // Stopped while selecting: nothing was moved yet. Selection is lost when the page closes.
                return;
            }

            foreach (FileItem missing in pending.Values)
            {
                if (sawWholeList)
                {
                    // Already gone (an earlier delete went through but was not recorded, or the user
                    // removed it by hand). Without this the copy would never be uploaded.
                    missing.OriginalDeleted = true;
                    Console.WriteLine($"Deletor: original no longer in the list, treating as binned: {missing.MediaKey} ({missing.Date})");
                }
                else
                {
                    Console.WriteLine($"Deletor: original not reached in the list, will retry next run: {missing.MediaKey} ({missing.Date})");
                }
            }

            files.Save();

            if (selected.Count == 0)
            {
                return;
            }

            ILocator deleteButton = await GetDeleteButton(page);
            await deleteButton.ClickAsync();

            ILocator confirmButton = page.Locator(Selectors.DIALOG_DEFAULT_BUTTON);
            await confirmButton.ClickAsync();
            await confirmButton.WaitForAsync(new() { State = WaitForSelectorState.Hidden });

            foreach (FileItem item in selected)
            {
                // A tile still on the page a while after the dialog closed was not moved to the bin.
                if (await WaitUntilGone(page.Locator(Selectors.TileByMediaKey(item.MediaKey)), 15_000) == false)
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

        private static async Task<bool> WaitUntilGone(ILocator locator, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (await locator.CountAsync() > 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                await Task.Delay(250);
            }

            return true;
        }

        private static async Task<ILocator> GetDeleteButton(IPage page)
        {
            ILocator deleteButtons = page.Locator(Selectors.MOVE_TO_BIN_BUTTON);
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
