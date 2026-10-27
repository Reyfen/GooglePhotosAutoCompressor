using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor.Modules
{
    internal class Downloader
    {
        public static async Task DownloadUncommresedFiles(IPage page, FileItemsData itemsData, CancellationToken cancellationToken)
        {
            Console.WriteLine("\n===\nDownloadUncommresedFiles\n===\n");

            Settings settings = Settings.Current;
            using SemaphoreSlim semaphore = new SemaphoreSlim(settings.ParallelDownloads);

            List<Task> downloadTasks = new();
            // Stops the scan: batch full, disk full, a zip, or the user pressed Stop.
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int filesToFinishProcessing = itemsData.Snapshot().Count(ItemRules.IsInProgress);

            // Our own uploaded copies show up in the list too; never compress them again.
            HashSet<string> ownCopies = itemsData.Snapshot()
                .Select(i => i.UploadedMediaKey)
                .OfType<string>()
                .ToHashSet();

            if (filesToFinishProcessing >= settings.ItemsPerBatch)
            {
                Console.WriteLine($"Downloader: {filesToFinishProcessing} items already in progress, finishing those first.");
                return;
            }

            try
            {
                await LargeFileLocator.EnumearteFiles(page, HandleFileItem, cts.Token);
            }
            finally
            {
                // Never leave download tasks running behind the next step, even when the scan failed.
                await Task.WhenAll(downloadTasks);
            }

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

                Directory.CreateDirectory(FilePaths.DOWNLOAD_DIR);
                if (DiskSpace.HasRoomFor(item.Size, DiskSpace.FreeBytes(FilePaths.DOWNLOAD_DIR), settings.MinFreeDiskBytes) == false)
                {
                    Console.WriteLine($"Downloader: not enough free disk space in {FilePaths.DOWNLOAD_DIR} " +
                                      $"(keeping {settings.MinFreeDiskGb} GB free). Stopping downloads for this run.");
                    cts.Cancel();
                    return;
                }

                filesToFinishProcessing++;

                if (filesToFinishProcessing >= settings.ItemsPerBatch)
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
                    Console.WriteLine($"Downloader: could not start download for {item.MediaKey}: {e.Message}");
                    await CountFailure(item);
                    itemsData.Save();
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
                        using (cancellationToken.Register(() => _ = download.CancelAsync()))
                        {
                            await download.SaveAsAsync(partPath);
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(partPath, path, true);
                        item.DownloadPath = path;
                        itemsData.Save();
                        Console.WriteLine($"ready: {path}");
                    }
                    catch (Exception e) when (cancellationToken.IsCancellationRequested)
                    {
                        // Stopped by the user: not the item's fault. The next run downloads it again.
                        Console.WriteLine($"Downloader: stopped {path} ({e.GetType().Name})");
                    }
                    catch (Exception e)
                    {
                        // One failed download must not stop the others.
                        Console.WriteLine($"Downloader: failed {path}: {e.Message}");
                        await CountFailure(item);
                        itemsData.Save();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));

                await Task.Delay(100);
            }
        }

        /// <summary>An item only loses an attempt when the internet was up; a dropped connection is not its fault.</summary>
        internal static async Task CountFailure(FileItem item)
        {
            if (await Network.IsOnlineAsync())
            {
                item.FailCount++;
            }
            else
            {
                Console.WriteLine($"Downloader: no internet, not counted as a failure: {item.MediaKey}");
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
