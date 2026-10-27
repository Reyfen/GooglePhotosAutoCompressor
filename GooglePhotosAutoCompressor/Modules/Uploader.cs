using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Uploader
    {
        private const float PROGRESS_APPEAR_TIMEOUT_MS = 30_000;
        private const float UPLOAD_TIMEOUT_MS = 30 * 60_000;

        internal static async Task UploadFiles(IPage page, FileItemsData files, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("\n===\nUploadFiles\n===\n");

            FileItem[] toUpload = files.Snapshot().Where(ItemRules.CanUpload).ToArray();
            if (toUpload.Length == 0)
            {
                return;
            }

            await page.GotoAsync($"https://photos.google.com/?hl={Const.LOCALE}");

            for (int i = 0; i < toUpload.Length && cancellationToken.IsCancellationRequested == false; i++)
            {
                FileItem file = toUpload[i];
                DateTime startedAt = DateTime.UtcNow;
                try
                {
                    IFileChooser fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
                    {
                        await page.Locator(Selectors.CREATE_BUTTON).ClickAsync();
                        await page.Locator(Selectors.IMPORT_FROM_COMPUTER).ClickAsync();
                    });
                    await fileChooser.SetFilesAsync(file.CompressPath);

                    bool finished = await WaitForUploadToFinish(page, cancellationToken);

                    // The progress bar also goes away when an upload fails, e.g. when the connection drops.
                    // Only trust it if the internet stayed up the whole time.
                    if (finished && (Network.WasOfflineSince(startedAt) || await Network.IsOnlineAsync() == false))
                    {
                        Console.WriteLine($"Uploader: the internet dropped during the upload of {file.CompressPath}; it will be uploaded again.");
                        finished = false;
                    }

                    if (finished == false)
                    {
                        // Not marked as uploaded: the next run uploads it again. Google Photos ignores
                        // an identical re-upload, and the date step also confirms uploads it finds.
                        Console.WriteLine($"Uploader: could not confirm upload of {file.CompressPath}");
                        continue;
                    }

                    MarkUploaded(file);
                    files.Save();
                    Console.WriteLine($"Uploader: [{i + 1}/{toUpload.Length}] uploaded {file.CompressPath}");
                }
                catch (PlaywrightException e)
                {
                    Console.WriteLine($"Uploader: failed for {file.CompressPath}: {e.Message}");
                }
            }
        }

        internal static void MarkUploaded(FileItem file)
        {
            file.CompressUploaded = true;
            // Photo copies keep their EXIF date taken (checked when compressed), so the date step is not needed.
            file.DateSet |= PhotoCompressor.IsSupported(file.CompressPath);
        }

        /// <summary>True when the progress indicator appeared and then went away.</summary>
        private static async Task<bool> WaitForUploadToFinish(IPage page, CancellationToken cancellationToken)
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

            // Wait in short steps so Stop is noticed; an upload left running when the browser closes is retried next run.
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(UPLOAD_TIMEOUT_MS);
            while (DateTime.UtcNow < deadline)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                try
                {
                    await uploadProgress.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 2000 });
                    return true;
                }
                catch (TimeoutException)
                {
                }
            }

            return false;
        }
    }
}
