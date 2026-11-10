using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class LargeFileLocator
    {
        /// <summary>No new tile for this long (and at least <see cref="MIN_STALLED_SCROLLS"/> scrolls) means the end of the list.</summary>
        private static readonly TimeSpan END_OF_LIST_AFTER = TimeSpan.FromSeconds(10);
        private const int MIN_STALLED_SCROLLS = 10;

        /// <summary>
        /// Scrolls through the large files list and calls <paramref name="process"/> once per item.
        /// Returns true when the whole list was seen (not cancelled, not cut off).
        /// </summary>
        public static async Task<bool> EnumearteFiles(IPage page, ProccesFileItemInLargeFilesList process, CancellationToken cancellationToken)
        {
            await page.GotoAsync($"https://photos.google.com/quotamanagement/large?hl={Const.LOCALE}");

            ILocator scrollView = page.Locator(Selectors.LARGE_FILES_SCROLL_CHILD).Locator("..");
            HashSet<string> seenKeys = new();
            HashSet<string> processedKeys = new();
            DateTime lastNewItemAt = DateTime.UtcNow;
            int stalledScrolls = 0;

            for (int scrollIteration = 0; scrollIteration < 20_000; scrollIteration++)
            {
                bool foundNew = false;
                // All tiles in view at once (key and text). Reading them one by one by position broke: selecting a
                // tile makes Google redraw the list, the count goes stale and "the 8th tile" no longer exists, so
                // reading it waited 30 s and the bin step failed (owner's run, 2026-11-10). Tiles are then found by key.
                TileSnapshot[] visible = await page.EvaluateAsync<TileSnapshot[]>(
                    "selector => Array.from(document.querySelectorAll(selector)).map(t => ({ key: t.getAttribute('data-media-key') || '', text: t.innerText || '' }))",
                    Selectors.LARGE_FILE_TILE);
                foreach (TileSnapshot snapshot in visible)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    string mediaKey = snapshot.Key;
                    if (string.IsNullOrEmpty(mediaKey) || processedKeys.Contains(mediaKey))
                    {
                        continue;
                    }

                    foundNew |= seenKeys.Add(mediaKey);

                    // Not parsed yet (text still loading): try again on the next scroll.
                    FileItem? item = TileParser.Parse(mediaKey, new[] { snapshot.Text });
                    if (item == null)
                    {
                        continue;
                    }

                    processedKeys.Add(mediaKey);
                    await process(page.Locator(Selectors.TileByMediaKey(mediaKey)).First, item);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (foundNew)
                {
                    lastNewItemAt = DateTime.UtcNow;
                    stalledScrolls = 0;
                }
                else if (++stalledScrolls >= MIN_STALLED_SCROLLS && DateTime.UtcNow - lastNewItemAt > END_OF_LIST_AFTER)
                {
                    return true;
                }

                var box = await scrollView.BoundingBoxAsync();
                if (box == null)
                {
                    return false;
                }

                await page.Mouse.MoveAsync(
                    box.X + box.Width / 2,
                    box.Y + box.Height / 2
                );

                await page.Mouse.WheelAsync(0, 200);
                await Task.Delay(100);
            }

            return false;
        }
    }

    internal delegate Task ProccesFileItemInLargeFilesList(ILocator tile, FileItem item);

    /// <summary>One tile as read from the page in a single call.</summary>
    internal sealed class TileSnapshot
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
    }
}
