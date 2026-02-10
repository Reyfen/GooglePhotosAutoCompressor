using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Deletor
    {
        internal static async Task DeleteCompressedFiles(IPage page, FileItemsData files)
        {
            Console.WriteLine("\n===\nDeleteCompressedFiles\n===\n");

            List<FileItem> storedFilesToDelete = new();
            using CancellationTokenSource cts = new();
            int notStoredItems = 0;
            await LargeFileLocator.EnumearteFiles(page, DeleteSelectedFile, cts.Token);

            if (storedFilesToDelete.Count == 0)
            {
                return;
            }

            ILocator deleteButton = await GetDeleteButton(page);

            await deleteButton.ClickAsync();
            await Task.Delay(100);
            ILocator finalDeletteButton = page.Locator("[data-mdc-dialog-button-default]");
            await finalDeletteButton.ClickAsync();

            foreach (var item in storedFilesToDelete)
            {
                item.OriginalDeleted = true;
            }
            files.Save();


            async Task DeleteSelectedFile(ILocator tile, FileItem item)
            {
                FileItem? storedItem = files.Items.FirstOrDefault(i => i.MediaKey == item.MediaKey && string.IsNullOrEmpty(i.CompressPath) == false);

                if (storedItem == null)
                {
                    notStoredItems++;
                    if (notStoredItems > 20)
                    {
                        cts.Cancel();
                    }

                    return;
                }

                storedFilesToDelete.Add(storedItem);

                await tile.ClickAsync();
                await Task.Delay(100);
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