namespace GooglePhotosAutoCompressor.Modules
{
    internal enum CompressResult
    {
        Ok,
        NotWorthIt,
        Failed,
    }

    internal class Compressor
    {
        private static readonly string[] VIDEO_EXTENSIONS = { ".mp4", ".mov" };

        /// <summary>Progress of the item being compressed: the item and 0..1.</summary>
        public static event Action<FileItem, double>? Progress;

        public static async Task CompressFiles(FileItemsData filesData, Ffmpeg ffmpeg, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("\n===\nCompressFiles\n===\n");

            Directory.CreateDirectory(FilePaths.COMPRESS_DIR);

            FileItem[] toCompress = filesData.Snapshot().Where(ItemRules.NeedsCompression).ToArray();
            List<string> summary = new();

            for (int i = 0; i < toCompress.Length && cancellationToken.IsCancellationRequested == false; i++)
            {
                FileItem file = toCompress[i];
                // Step progress: finished files plus the share of the current one (ffmpeg reports it).
                int index = i;
                void OnFile(FileItem f, double fraction) => StepProgress.Report(index + fraction, toCompress.Length);
                Progress += OnFile;
                CompressResult result;
                try
                {
                    result = await Task.Run(() => CompressOne(ffmpeg, file, FilePaths.COMPRESS_DIR, cancellationToken), CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    // Stopped by the user. The half-written copy is already deleted; the next run starts it again.
                    Console.WriteLine($"Compressor: stopped while compressing {file.DownloadPath}");
                    break;
                }
                finally
                {
                    Progress -= OnFile;
                    StepProgress.Report(i + 1, toCompress.Length);
                }

                filesData.Save();

                string log = $"[{i + 1}/{toCompress.Length}] " + result switch
                {
                    CompressResult.Ok => $"ok {file.CompressPath} \t {DescribeSaving(file)}",
                    CompressResult.NotWorthIt => $"skipped, would not save space or unsupported format: {file.DownloadPath}",
                    _ => $"failed ({file.FailCount}/{Settings.Current.MaxFails}): {file.DownloadPath}",
                };
                Console.WriteLine($"Compressor: {log}");
                summary.Add(log);
            }

            if (summary.Count > 1)
            {
                Console.WriteLine($"\nCompressor: summary\n{string.Join('\n', summary)}");
            }
        }

        private static string DescribeSaving(FileItem file)
        {
            long before = File.Exists(file.DownloadPath) ? new FileInfo(file.DownloadPath).Length : file.Size;
            long after = new FileInfo(file.CompressPath).Length;
            return $"{before / 1024.0 / 1024:F1} MB -> {after / 1024.0 / 1024:F1} MB";
        }

        /// <summary>
        /// Produces a checked compressed copy for one item and updates its fields.
        /// The output is written to a .part file and only renamed once it passed the checks,
        /// so a crash never leaves a cut-off file that looks finished.
        /// </summary>
        internal static CompressResult CompressOne(Ffmpeg ffmpeg, FileItem file, string compressDir, CancellationToken cancellationToken = default)
        {
            AdoptFinishedCopy(file, compressDir);

            CompressResult result = CompressOneUnchecked(ffmpeg, file, compressDir, cancellationToken);
            if (result == CompressResult.Ok && File.Exists(file.CompressPath))
            {
                file.CompressedSize = new FileInfo(file.CompressPath).Length;
            }

            return result;
        }

        /// <summary>
        /// The app can be closed after a copy was renamed into place but before that was saved to the
        /// state file. Pick that copy up (it is checked again below) instead of compressing again.
        /// </summary>
        internal static void AdoptFinishedCopy(FileItem file, string compressDir)
        {
            if (string.IsNullOrEmpty(file.CompressPath) == false || File.Exists(file.DownloadPath) == false)
            {
                return;
            }

            string expected = FilePaths.CompressPath(compressDir, file.MediaKey, file.DownloadPath);
            if (File.Exists(expected))
            {
                file.CompressPath = expected;
            }
        }

        private static CompressResult CompressOneUnchecked(Ffmpeg ffmpeg, FileItem file, string compressDir, CancellationToken cancellationToken)
        {
            // The downloaded file decides, not the tile: a video tile whose duration had not loaded looks like a photo.
            if (PhotoCompressor.IsSupported(file.DownloadPath) || (File.Exists(file.DownloadPath) == false && PhotoCompressor.IsSupported(file.CompressPath)))
            {
                return PhotoCompressor.CompressOne(file, compressDir);
            }

            bool hasSource = File.Exists(file.DownloadPath);

            // A copy from an earlier run (possibly an older version of this app): check it first.
            if (string.IsNullOrEmpty(file.CompressPath) == false && File.Exists(file.CompressPath))
            {
                if (Check(ffmpeg, file, file.CompressPath) == CompressResult.Ok)
                {
                    file.Verified = true;
                    return CompressResult.Ok;
                }

                if (hasSource == false)
                {
                    return Fail(file);
                }

                File.Delete(file.CompressPath);
            }

            file.CompressPath = null;

            if (hasSource == false)
            {
                return Fail(file);
            }

            if (VIDEO_EXTENSIONS.Contains(Path.GetExtension(file.DownloadPath).ToLowerInvariant()) == false)
            {
                file.CompressionSkipped = true;
                return CompressResult.NotWorthIt;
            }

            Directory.CreateDirectory(compressDir);
            string path = FilePaths.CompressPath(compressDir, file.MediaKey, file.DownloadPath);
            string partPath = Path.ChangeExtension(path, ".part" + Path.GetExtension(path));

            CompressResult result;
            try
            {
                // Copying audio keeps quality; some audio codecs cannot go into mp4, so retry with AAC.
                bool encoded = Encode(ffmpeg, file, partPath, copyAudio: true, cancellationToken)
                               || Encode(ffmpeg, file, partPath, copyAudio: false, cancellationToken);

                result = encoded ? Check(ffmpeg, file, partPath) : CompressResult.Failed;
            }
            catch (OperationCanceledException)
            {
                File.Delete(partPath);
                throw;
            }

            if (result != CompressResult.Ok)
            {
                File.Delete(partPath);

                if (result == CompressResult.NotWorthIt)
                {
                    file.CompressionSkipped = true;
                    return result;
                }

                return Fail(file);
            }

            File.Move(partPath, path, true);
            file.CompressPath = path;
            file.Verified = true;
            return CompressResult.Ok;
        }

        private static CompressResult Fail(FileItem file)
        {
            file.FailCount++;
            return CompressResult.Failed;
        }

        /// <summary>Output must play for as long as the original and be clearly smaller.</summary>
        private static CompressResult Check(Ffmpeg ffmpeg, FileItem file, string outputPath)
        {
            TimeSpan? outputDuration = ffmpeg.GetDuration(outputPath);
            if (outputDuration == null)
            {
                return CompressResult.Failed;
            }

            bool hasSource = File.Exists(file.DownloadPath);
            TimeSpan expected = (hasSource ? ffmpeg.GetDuration(file.DownloadPath) : null) ?? file.Duration;
            long originalSize = hasSource ? new FileInfo(file.DownloadPath).Length : file.Size;

            if (IsDurationClose(expected, outputDuration.Value) == false)
            {
                Console.WriteLine($"Compressor: duration mismatch {outputDuration:g} vs {expected:g} for {outputPath}");
                return CompressResult.Failed;
            }

            if (new FileInfo(outputPath).Length > originalSize * Settings.Current.MaxCompressedSizeRatio)
            {
                return CompressResult.NotWorthIt;
            }

            return CompressResult.Ok;
        }

        /// <summary>Allows 1 s or 2 %, whichever is larger (tile durations are rounded to seconds).</summary>
        internal static bool IsDurationClose(TimeSpan expected, TimeSpan actual)
        {
            double tolerance = Math.Max(1.0, expected.TotalSeconds * 0.02);
            return Math.Abs(expected.TotalSeconds - actual.TotalSeconds) <= tolerance;
        }

        private static bool Encode(Ffmpeg ffmpeg, FileItem file, string outputPath, bool copyAudio, CancellationToken cancellationToken)
        {
            Settings settings = Settings.Current;
            double totalSeconds = Math.Max(1, file.Duration.TotalSeconds);
            Progress?.Invoke(file, 0);

            (int exitCode, _) = ffmpeg.Run(
                BuildArgs(file.DownloadPath, outputPath, copyAudio, settings.VideoResolution, settings.VideoCrf),
                onLine: line =>
                {
                    if (Ffmpeg.ParseProgressTime(line) is TimeSpan done)
                    {
                        Progress?.Invoke(file, Math.Clamp(done.TotalSeconds / totalSeconds, 0, 1));
                    }
                },
                cancellationToken: cancellationToken);

            return exitCode == 0 && File.Exists(outputPath);
        }

        internal static IEnumerable<string> BuildArgs(string inputPath, string outputPath, bool copyAudio,
            VideoResolution resolution = VideoResolution.Half, int crf = 20)
        {
            var args = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-stats", "-y",
                "-i", inputPath,
                "-vf", ScaleFilter(resolution),
                "-c:v", "libx264", "-crf", crf.ToString(System.Globalization.CultureInfo.InvariantCulture), "-preset", "medium",
            };

            args.AddRange(copyAudio ? new[] { "-c:a", "copy" } : new[] { "-c:a", "aac", "-b:a", "192k" });

            // Keep creation time, location and other tags from the original.
            args.AddRange(new[] { "-map_metadata", "0", "-movflags", "+use_metadata_tags", outputPath });
            return args;
        }

        /// <summary>
        /// Sizes are rounded down to even numbers (libx264 rejects odd sizes). The "max" modes limit the
        /// shorter side, so portrait and landscape videos are treated the same, and never scale up.
        /// </summary>
        internal static string ScaleFilter(VideoResolution resolution) => resolution switch
        {
            VideoResolution.Max1080p => ShorterSideAtMost(1080),
            VideoResolution.Max720p => ShorterSideAtMost(720),
            _ => "scale=trunc(iw/4)*2:trunc(ih/4)*2",
        };

        private static string ShorterSideAtMost(int pixels) =>
            $"scale='if(gt(iw,ih),-2,trunc(min(iw,{pixels})/2)*2)':'if(gt(iw,ih),trunc(min(ih,{pixels})/2)*2,-2)'";
    }
}
