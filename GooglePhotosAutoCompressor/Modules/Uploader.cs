using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Uploader
    {
        private const float PROGRESS_APPEAR_TIMEOUT_MS = 30_000;
        private const float UPLOAD_TIMEOUT_MS = 30 * 60_000;

        internal static async Task UploadFiles(IPage page, FileItemsData files)
        {
            Console.WriteLine("\n===\nUploadFiles\n===\n");

            FileItem[] toUpload = files.Items.Where(ItemRules.CanUpload).ToArray();
            if (toUpload.Length == 0)
            {
                return;
            }

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");

            foreach (FileItem file in toUpload)
            {
                try
                {
                    IFileChooser fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
                    {
                        await page.Locator(Selectors.CREATE_BUTTON).ClickAsync();
                        await page.Locator(Selectors.IMPORT_FROM_COMPUTER).ClickAsync();
                    });
                    await fileChooser.SetFilesAsync(file.CompressPath);

                    if (await WaitForUploadToFinish(page) == false)
                    {
                        // Not marked as uploaded: the next run uploads it again. Google Photos ignores
                        // an identical re-upload, and the date step also confirms uploads it finds.
                        Console.WriteLine($"Uploader: could not confirm upload of {file.CompressPath}");
                        continue;
                    }

                    file.CompressUploaded = true;
                    // Photo copies keep their EXIF date taken (checked when compressed), so the date step is not needed.
                    file.DateSet |= PhotoCompressor.IsSupported(file.CompressPath);
                    files.Save();
                    Console.WriteLine($"Uploader: uploaded {file.CompressPath}");
                }
                catch (PlaywrightException e)
                {
                    Console.WriteLine($"Uploader: failed for {file.CompressPath}: {e.Message}");
                }
            }
        }

        /// <summary>True when the progress indicator appeared and then went away.</summary>
        private static async Task<bool> WaitForUploadToFinish(IPage page)
        {
            ILocator uploadProgress = page.Locator(Selectors.UPLOAD_PROGRESS).First;

            try
            {
                await uploadProgress.WaitForAsync(new() { Timeout = PROGRESS_APPEAR_TIMEOUT_MS });
            }
            catch (TimeoutException)
            {
                return false;
            }

            await uploadProgress.WaitForAsync(new()
            {
                State = WaitForSelectorState.Detached,
                Timeout = UPLOAD_TIMEOUT_MS,
            });
            return true;
        }
    }
}
