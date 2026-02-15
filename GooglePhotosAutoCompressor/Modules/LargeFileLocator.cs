using Microsoft.Playwright;
using System.Globalization;

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
                bool anyAdded = false;
                for (int tileIndex = 0; tileIndex < tilesCount && cancellationToken.IsCancellationRequested == false; tileIndex++)
                {
                    ILocator tile = tiles.Nth(tileIndex);
                    FileItem? item = await ParseFileItemFromTile(tile);
                    if (item == null || duplicatePreventSet.Add(item) == false)
                    {
                        continue;
                    }

                    anyAdded = true;
                    await process(tile, item);
                }

                if (anyAdded == false || cancellationToken.IsCancellationRequested)
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

            string[] texts = string.Join('\n', await tile.AllInnerTextsAsync()).ToLower().Split('\n');

            int sizeIndex = Array.FindIndex(texts, t => t.EndsWith("kb") || t.EndsWith("mb") || t.EndsWith("gb"));
            if (sizeIndex < 0)
            {
                return null;
            }

            long size = ParseSizeToBytes(texts[sizeIndex]);

            if (size <= 0)
            {
                return null;
            }

            texts[sizeIndex] = string.Empty;

            DateOnly date = default;
            int dateIndex = Array.FindIndex(texts, t => DateOnly.TryParseExact(t, Const.DATE_FORMATS, CultureInfo.InvariantCulture, DateTimeStyles.None, out date));
            if (dateIndex < 0)
            {
                return null;
            }

            texts[dateIndex] = string.Empty;

            TimeSpan duration = default;
            MediaType mediaType = MediaType.Video;
            int durationIndex = Array.FindIndex(texts, t => TimeSpan.TryParseExact(t, Const.TIME_FORMATS, CultureInfo.InvariantCulture, out duration));
            if (durationIndex < 0)
            {
                mediaType = MediaType.Image;
            }

            return new (mediaType, mediaKey, date, duration, size);
        }

        private static long ParseSizeToBytes(string input)
        {
            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new FormatException("Invalid size format");

            double value = double.Parse(parts[0], CultureInfo.InvariantCulture);
            string unit = parts[1].ToUpperInvariant();

            return unit switch
            {
                "B" => (long)value,
                "KB" => (long)(value * 1024),
                "MB" => (long)(value * 1024 * 1024),
                "GB" => (long)(value * 1024 * 1024 * 1024),
                _ => throw new FormatException($"Unknown unit: {unit}")
            };
        }
    }

    internal delegate Task ProccesFileItemInLargeFilesList(ILocator tile, FileItem item);
}
