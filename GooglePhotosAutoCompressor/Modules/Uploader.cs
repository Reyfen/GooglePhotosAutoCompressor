using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Uploader
    {
        internal static async Task UploadFiles(IPage page, FileItemsData files)
        {
            Console.WriteLine("\n===\nUploadFiles\n===\n");

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");

            foreach (var file in files.Items)
            {
                if (file.CompressUploaded || string.IsNullOrEmpty(file.CompressPath))
                {
                    continue;
                }

                var fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
                {
                    await page.Locator("[aria-label='Create and add photos']").ClickAsync();
                    await page.Locator("[aria-label='Import photos from your computer']").ClickAsync();
                });
                await fileChooser.SetFilesAsync(file.CompressPath);
                await WaitForUploadToFinish(page);

                file.CompressUploaded = true;
                files.Save();
            }
        }

        private static async Task WaitForUploadToFinish(IPage page)
        {
            ILocator uploadProgress = page.Locator("[aria-live='polite'] [data-progressvalue]");
            await uploadProgress.First.WaitForAsync();
            await uploadProgress.First.WaitForAsync(new()
            {
                State = WaitForSelectorState.Detached
            });
        }
    }
}