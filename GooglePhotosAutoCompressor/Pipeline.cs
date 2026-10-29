using GooglePhotosAutoCompressor.Modules;
using Microsoft.Playwright;

namespace GooglePhotosAutoCompressor
{
    internal enum PipelineStage
    {
        Idle,
        Preparing,
        SigningIn,
        Downloading,
        Compressing,
        ReadingDates,
        ConfirmingBin,
        MovingToBin,
        Uploading,
        SettingDates,
        CleaningUp,
        WaitingForInternet,
        Finished,
    }

    internal enum RunResult
    {
        Finished,
        Stopped,
        Failed,
    }

    /// <summary>How the pipeline talks to whoever runs it (window or console).</summary>
    internal sealed class PipelineHost
    {
        public Func<string, Task<bool>> AskYesNo { get; init; } = FfmpegInstaller.AskOnConsole;

        /// <summary>Shown before originals go to the bin (when <see cref="Settings.ConfirmBeforeBin"/> is on). False skips the bin and upload this run.</summary>
        public Func<IReadOnlyList<FileItem>, Task<bool>> ConfirmBin { get; init; } = _ => Task.FromResult(true);

        /// <summary>Stage and a short message for the user.</summary>
        public Action<PipelineStage, string> StageChanged { get; init; } = (_, message) => Console.WriteLine($"--- {message}");

        /// <summary>The state file was loaded; the UI can show and watch the items.</summary>
        public Action<FileItemsData> StateLoaded { get; init; } = _ => { };
    }

    /// <summary>
    /// One run: sign in, then batches of download → compress → bin → upload → date.
    ///
    /// Every step only acts on what the state file says is ready, and the state file is saved after every
    /// change, so a run can be stopped, killed or cut off from the internet at any point and the next run
    /// carries on. This class adds: Stop at safe points, waiting for the internet, and retrying a step.
    /// </summary>
    internal sealed class Pipeline
    {
        private const int MAX_STEP_ATTEMPTS = 3;

        private readonly PipelineHost _host;
        private int _errors;

        public Pipeline(PipelineHost host)
        {
            _host = host;
        }

        /// <summary>The message of the last error, for the summary.</summary>
        public string? LastError { get; private set; }

        public async Task<RunResult> RunAsync(Settings settings, CancellationToken cancellationToken)
        {
            Settings.Current = settings.Normalized();
            _errors = 0;
            LastError = null;

            using CancellationTokenSource monitorCts = new();
            Task monitor = Network.MonitorAsync(monitorCts.Token);
            IPlaywright? playwright = null;

            try
            {
                Stage(PipelineStage.Preparing, "Getting ready");

                int partial = FilePaths.DeletePartialFiles(FilePaths.DOWNLOAD_DIR, FilePaths.COMPRESS_DIR);
                if (partial > 0)
                {
                    Console.WriteLine($"Removed {partial} half-written file(s) left by an earlier run that was interrupted.");
                }

                string? ffmpegPath = await FfmpegInstaller.FindOrOfferDownload(_host.AskYesNo);
                if (ffmpegPath == null)
                {
                    return Fail("ffmpeg is needed to compress videos. Install it, or allow the download, and start again.");
                }

                Ffmpeg ffmpeg = new(ffmpegPath);
                FileItemsData files = new();
                _host.StateLoaded(files);

                await WaitForInternet(cancellationToken);

                Stage(PipelineStage.Preparing, "Opening Chrome");
                (playwright, IPage page) = await Initlizer.OpenBrowser();

                Stage(PipelineStage.SigningIn, "Opening Google Photos");
                await Step(PipelineStage.SigningIn, "Opening Google Photos",
                    () => Initlizer.Login(page, cancellationToken,
                        () => Stage(PipelineStage.SigningIn, "Sign in to Google in the Chrome window. The app waits for you.")),
                    cancellationToken, stopOnError: true);

                for (int batch = 1; ; batch++)
                {
                    string before = Fingerprint(files);
                    string prefix = Settings.Current.RepeatUntilDone ? $"Batch {batch}: " : "";

                    await Step(PipelineStage.Downloading, prefix + "Finding and downloading large items",
                        () => Downloader.DownloadUncommresedFiles(page, files, cancellationToken), cancellationToken);
                    await Step(PipelineStage.Compressing, prefix + "Compressing",
                        () => Compressor.CompressFiles(files, ffmpeg, cancellationToken), cancellationToken);

                    await Step(PipelineStage.ReadingDates, prefix + "Reading date and time of the originals",
                        () => TakenTimeReader.ReadTakenTimes(page, files, cancellationToken), cancellationToken);

                    if (await ConfirmBin(files, cancellationToken))
                    {
                        await Step(PipelineStage.MovingToBin, prefix + "Moving originals to the bin",
                            () => Deletor.DeleteCompressedFiles(page, files, cancellationToken), cancellationToken);
                    }

                    await Step(PipelineStage.Uploading, prefix + "Uploading compressed copies",
                        () => Uploader.UploadFiles(page, files, cancellationToken), cancellationToken);
                    await Step(PipelineStage.SettingDates, prefix + "Setting dates of the copies",
                        () => DateSetter.SetCompressedFilesDates(page, files, cancellationToken), cancellationToken);

                    if (Settings.Current.DeleteLocalFilesWhenDone)
                    {
                        Stage(PipelineStage.CleaningUp, "Deleting local files of finished items");
                        LocalFiles.DeleteFinished(files);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (Settings.Current.RepeatUntilDone == false || _errors > 0)
                    {
                        break;
                    }

                    if (Fingerprint(files) == before)
                    {
                        Console.WriteLine("Nothing changed in this batch: all done for now.");
                        break;
                    }
                }

                Stage(PipelineStage.Finished, _errors == 0 ? "Finished" : $"Finished with {_errors} error(s)");
                return _errors == 0 ? RunResult.Finished : RunResult.Failed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Stage(PipelineStage.Finished, "Stopped. Start again any time to carry on.");
                return RunResult.Stopped;
            }
            catch (BrowserClosedException)
            {
                return Fail("The Chrome window was closed. Start again to carry on.");
            }
            catch (Exception e) when (e is PlaywrightException or TimeoutException or InvalidOperationException or IOException
                                          or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
            {
                Console.WriteLine(e);
                return Fail(e.Message);
            }
            finally
            {
                monitorCts.Cancel();
                await monitor;
                playwright?.Dispose();
            }
        }

        /// <summary>
        /// Runs one step. A failure while offline waits for the internet and retries the step (every step
        /// picks up where it was). Any other failure is logged and the run goes on with the next step:
        /// each step only touches items the earlier steps finished, so that is safe.
        /// </summary>
        private async Task Step(PipelineStage stage, string message, Func<Task> action, CancellationToken cancellationToken, bool stopOnError = false)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stage(stage, message);

                try
                {
                    await action();
                    return;
                }
                catch (Exception e) when (cancellationToken.IsCancellationRequested == false && e is not OperationCanceledException)
                {
                    if (IsBrowserClosed(e))
                    {
                        throw new BrowserClosedException();
                    }

                    if (await Network.IsOnlineAsync(cancellationToken) == false && attempt < MAX_STEP_ATTEMPTS + 5)
                    {
                        Console.WriteLine($"{message}: lost the internet connection ({FirstLine(e.Message)}).");
                        await WaitForInternet(cancellationToken);
                        continue;
                    }

                    if (attempt < MAX_STEP_ATTEMPTS && e is PlaywrightException or TimeoutException)
                    {
                        Console.WriteLine($"{message}: {FirstLine(e.Message)}. Trying again.");
                        continue;
                    }

                    _errors++;
                    LastError = $"{message}: {FirstLine(e.Message)}";
                    Console.WriteLine($"Error in step \"{message}\": {e}");
                    if (stopOnError)
                    {
                        throw;
                    }

                    return;
                }
            }
        }

        private async Task<bool> ConfirmBin(FileItemsData files, CancellationToken cancellationToken)
        {
            List<FileItem> toBin = files.Snapshot().Where(ItemRules.IsReadyForBin).ToList();
            if (toBin.Count == 0 || Settings.Current.ConfirmBeforeBin == false)
            {
                return true;
            }

            Stage(PipelineStage.ConfirmingBin, $"Waiting for you: move {toBin.Count} original(s) to the bin?");
            bool ok = await _host.ConfirmBin(toBin);
            cancellationToken.ThrowIfCancellationRequested();
            if (ok == false)
            {
                Console.WriteLine("Moving to the bin was not confirmed. The copies are kept on this PC for the next run.");
            }

            return ok;
        }

        private async Task WaitForInternet(CancellationToken cancellationToken)
        {
            await Network.WaitUntilOnlineAsync(cancellationToken,
                () => Stage(PipelineStage.WaitingForInternet, "No internet connection. Waiting for it to come back..."));
        }

        private RunResult Fail(string message)
        {
            LastError = message;
            Stage(PipelineStage.Finished, message);
            return RunResult.Failed;
        }

        private void Stage(PipelineStage stage, string message) => _host.StageChanged(stage, message);

        /// <summary>Changes whenever any item moves forward, so "repeat until done" knows when to stop.</summary>
        private static string Fingerprint(FileItemsData files) =>
            string.Join(',', files.Snapshot().Select(i => (int)ItemRules.Describe(i)).GroupBy(s => s).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));

        internal static bool IsBrowserClosed(Exception e)
        {
            for (Exception? inner = e; inner != null; inner = inner.InnerException)
            {
                if (inner.GetType().Name == "TargetClosedException"
                    || inner.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase)
                    || inner.Message.Contains("Browser has been closed", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstLine(string text) => text.Split('\n')[0].Trim();

        private sealed class BrowserClosedException : Exception
        {
        }
    }

    internal static class LocalFiles
    {
        /// <summary>
        /// Deletes the local original and copy of finished items (copy uploaded and dated, original in the
        /// Google Photos bin). Returns the bytes freed.
        /// </summary>
        public static long DeleteFinished(FileItemsData files)
        {
            long freed = 0;
            foreach (FileItem item in files.Snapshot().Where(ItemRules.IsDone))
            {
                foreach (string? path in new[] { item.DownloadPath, item.CompressPath })
                {
                    if (string.IsNullOrEmpty(path) || File.Exists(path) == false)
                    {
                        continue;
                    }

                    try
                    {
                        long size = new FileInfo(path).Length;
                        File.Delete(path);
                        freed += size;

                        // Downloads live in a folder per item.
                        string? dir = Path.GetDirectoryName(path);
                        if (dir != null && path == item.DownloadPath && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() == false)
                        {
                            Directory.Delete(dir);
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Console.WriteLine($"Could not delete {path}: {e.Message}");
                    }
                }
            }

            if (freed > 0)
            {
                Console.WriteLine($"Freed {freed / 1024.0 / 1024:F0} MB on this PC.");
            }

            return freed;
        }

        /// <summary>Size of the download and compress folders.</summary>
        public static long CacheSize()
        {
            long total = 0;
            foreach (string dir in new[] { FilePaths.DOWNLOAD_DIR, FilePaths.COMPRESS_DIR }.Where(Directory.Exists))
            {
                try
                {
                    total += Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }

            return total;
        }
    }
}
