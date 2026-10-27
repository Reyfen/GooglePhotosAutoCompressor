using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class LargeFileLocator
    {
        public static async Task EnumearteFiles(IPage page, ProccesFileItemInLargeFilesList process, CancellationToken cancellationToken)
        {
            await page.GotoAsync($"https://photos.google.com/quotamanagement/large?hl={Const.LOCALE}");

            ILocator scrollView = page.Locator("[data-p*='mediaKey']").Locator("..");
            HashSet<FileItem> duplicatePreventSet = new ();

            for (int scrollIteration = 0; scrollIteration < 1000; scrollIteration++)
            {
                ILocator tiles = page.Locator("[data-media-key]:has(div[role='checkbox'])");
                int tilesCount = await tiles.CountAsync();
                for (int tileIndex = 0; tileIndex < tilesCount && cancellationToken.IsCancellationRequested == false; tileIndex++)
                {
                    ILocator tile = tiles.Nth(tileIndex);
                    FileItem? item = await ParseFileItemFromTile(tile);
                    if (item == null || duplicatePreventSet.Add(item) == false)
                    {
                        continue;
                    }

                    await process(tile, item);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var box = await scrollView.BoundingBoxAsync();

                await page.Mouse.MoveAsync(
                    box.X + box.Width / 2,
                    box.Y + box.Height / 2
                );

                await page.Mouse.WheelAsync(0, 200);
                await Task.Delay(100);
            }
        }

        private static async Task<FileItem?> ParseFileItemFromTile(ILocator tile)
        {
            string? mediaKey = await tile.GetAttributeAsync("data-media-key");

            if (string.IsNullOrEmpty(mediaKey))
            {
                return null;
            }

            return TileParser.Parse(mediaKey, await tile.AllInnerTextsAsync());
        }
    }

    internal delegate Task ProccesFileItemInLargeFilesList(ILocator tile, FileItem item);
}
