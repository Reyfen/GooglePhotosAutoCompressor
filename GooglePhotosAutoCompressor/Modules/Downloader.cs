using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Downloader
    {
        public static async Task<FileItemsData> DownloadUncommresedFiles(IPage page)
        {
            Console.WriteLine("\n===\nDownloadUncommresedFiles\n===\n");

            FileItemsData itemsData = new ();

            using SemaphoreSlim semaphore = new SemaphoreSlim(5);

            List<Task> downloadTasks = new();
            using CancellationTokenSource cts = new();
            int filesToFinishProcessing = itemsData.Items.Count(ItemRules.IsInProgress);

            // Our own uploaded copies show up in the list too; never compress them again.
            HashSet<string> ownCopies = itemsData.Items
                .Select(i => i.UploadedMediaKey)
                .OfType<string>()
                .ToHashSet();

            if (filesToFinishProcessing >= Const.TOTAL_FILES_TO_PROCESS_IN_ONE_BATCH)
            {
                return itemsData;
            }

            await LargeFileLocator.EnumearteFiles(page, HandleFileItem, cts.Token);

            await Task.WhenAll(downloadTasks);

            return itemsData;

            async Task HandleFileItem(ILocator tile, FileItem item)
            {
                if (ownCopies.Contains(item.MediaKey))
                {
                    return;
                }

                item = itemsData.AddOrGet(item);
                itemsData.Save();

                if (ItemRules.IsCompressionReasonable(item) == false || ItemRules.NeedsDownload(item) == false)
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

                IDownload? download;
                string path;

                await semaphore.WaitAsync();
                try
                {
                    await tile.ClickAsync();
                    await Task.Delay(100);
                    (download, path) = await GetFileDownloader(page, item.MediaKey);
                }
                catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException)
                {
                    semaphore.Release();
                    item.FailCount++;
                    itemsData.Save();
                    Console.WriteLine($"Downloader: could not start download for {item.MediaKey}: {e.Message}");
                    return;
                }

                string fileName = Path.GetFileName(path);

                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Several tiles were selected at once, so Google sent a zip. Selection is out of sync; stop.
                    semaphore.Release();
                    if (download != null)
                    {
                        await download.CancelAsync();
                    }

                    cts.Cancel();
                    Console.WriteLine("Downloader: got a zip (several items selected); stopping the scan for this run.");
                    return;
                }

                if (FilePaths.IsCompressedName(fileName))
                {
                    // A copy this app uploaded before it recorded media keys. Leave it alone.
                    semaphore.Release();
                    if (download != null)
                    {
                        await download.CancelAsync();
                    }

                    item.CompressionSkipped = true;
                    itemsData.Save();
                    Console.WriteLine($"Downloader: skipped own copy {fileName}");
                    return;
                }

                downloadTasks.Add(Task.Run(async () =>
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
                        // One failed download must not stop the others.
                        item.FailCount++;
                        itemsData.Save();
                        Console.WriteLine($"Downloader: failed {path}: {e.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));

                await Task.Delay(100);
            }
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
            ILocator downloadButtons = page.Locator(Selectors.DOWNLOAD_BUTTON);
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
