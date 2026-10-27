using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Downloader
    {
        public static async Task<FileItemsData> DownloadUncommresedFiles(IPage page)
        {
            Console.WriteLine("\n===\nDownloadUncommresedFiles\n===\n");

            ILocator scrollView = page.Locator("[data-p*='mediaKey']").Locator("..");

            FileItemsData itemsData = new ();

            using SemaphoreSlim semaphore = new SemaphoreSlim(5);

            List<Task> downloadTasks = new();
            using CancellationTokenSource cts = new();
            int filesToFinishProcessing = itemsData.Items.Count(i => IsCompressionReasonable(i) && (IsDownloadNeeded(i) || i.DateSet == false));
            var x = itemsData.Items.Where(i => IsCompressionReasonable(i) && (IsDownloadNeeded(i) || i.DateSet == false)).ToArray();

            if (filesToFinishProcessing >= Const.TOTAL_FILES_TO_PROCESS_IN_ONE_BATCH)
            {
                return itemsData;
            }

            await LargeFileLocator.EnumearteFiles(page, HandleFileItem, cts.Token);

            await Task.WhenAll(downloadTasks);

            return itemsData;

            async Task HandleFileItem(ILocator tile, FileItem item)
            {
                item = itemsData.AddOrGet(item);
                itemsData.Save();

                if (IsCompressionReasonable(item) == false || IsDownloadNeeded(item) == false)
                {
                    return;
                }

                filesToFinishProcessing++;

                if (filesToFinishProcessing >= Const.TOTAL_FILES_TO_PROCESS_IN_ONE_BATCH)
                {
                    cts.Cancel();
                }

                item.DownloadPath = null;
                item.CompressPath = null;
                itemsData.Save();

                await tile.ClickAsync();
                await Task.Delay(100);

                await semaphore.WaitAsync();
                (IDownload? download, string path) = await GetFileDownloader(page, item.MediaKey);

                Task downloadTask = Task.Run(async () =>
                {
                    try
                    {
                        if (download == null)
                        {
                            Console.WriteLine($"exist: {path}");
                            item.DownloadPath = path;
                            itemsData.Save();
                            return;
                        }

                        Console.WriteLine($"start: {path}");
                        // Save under a temp name so a crash never leaves a half file that looks finished.
                        string partPath = path + ".part";
                        await download.SaveAsAsync(partPath);
                        File.Move(partPath, path, true);
                        item.DownloadPath = path;
                        itemsData.Save();
                        Console.WriteLine($"ready: {path}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                });
                downloadTasks.Add(downloadTask);

                await Task.Delay(100);
            }
        }

        private static bool IsCompressionReasonable(FileItem item)
        {
            double sizePerSec = item.Size / item.Duration.TotalSeconds;

            return sizePerSec >= Const.MIN_SIZE_PER_SEC_TO_COMPRESS;
        }

        private static bool IsDownloadNeeded(FileItem item)
        {
            if (item.CompressUploaded || item.DateSet)
            {
                return false;
            }

            if (string.IsNullOrEmpty(item.DownloadPath) == false && File.Exists(item.DownloadPath))
            {
                return false;
            }

            if (string.IsNullOrEmpty(item.CompressPath) == false && File.Exists(item.CompressPath))
            {
                return false;
            }

            return true;
        }

        private static async Task<(IDownload? download, string path)> GetFileDownloader(IPage page, string mediaKey)
        {
            ILocator downloadButton = await GetDownloadButton(page);

            // Start waiting before the click so a fast download event is not missed.
            IDownload download = await page.RunAndWaitForDownloadAsync(() => downloadButton.ClickAsync());
            string path = FilePaths.DownloadPath(FilePaths.DOWNLOAD_DIR, mediaKey, download.SuggestedFilename);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path))
            {
                await download.CancelAsync();
                return (null, path);
            }

            return (download, path);

        }

        private static async Task<ILocator> GetDownloadButton(IPage page)
        {
            ILocator downloadButtons = page.Locator("[aria-label='Download']");
            int count = await downloadButtons.CountAsync();

            for (int i = 0; i < count; i++)
            {
                var button = downloadButtons.Nth(i);
                var box = await button.BoundingBoxAsync();

                if (box is null || box.Width <= 0)
                {
                    continue;
                }

                return button;
            }

            throw new Exception("download button not found");
        }
    }
}
