using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Runs the real ffmpeg on small generated clips. Skipped when ffmpeg is not installed.</summary>
    public sealed class CompressorTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-compress-").FullName;
        private readonly Ffmpeg? _ffmpeg = Ffmpeg.FindExecutable() is { } path ? new Ffmpeg(path) : null;

        public void Dispose() => Directory.Delete(_dir, true);

        private Ffmpeg RequireFfmpeg()
        {
            Skip.If(_ffmpeg == null, "ffmpeg not found");
            return _ffmpeg!;
        }

        /// <summary>Makes a test clip. Busy picture at high quality, so compressing it saves space.</summary>
        private string MakeClip(string name, int seconds = 3, string size = "640x360", int crf = 5)
        {
            string path = Path.Combine(_dir, name);
            (int exit, string err) = _ffmpeg!.Run(new[]
            {
                "-hide_banner", "-y",
                "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=30:duration={seconds}",
                "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
                "-c:v", "libx264", "-crf", crf.ToString(), "-pix_fmt", "yuv444p",
                "-c:a", "aac", "-shortest",
                "-metadata", "creation_time=2024-05-01T10:00:00.000000Z",
                path,
            });
            Assert.True(exit == 0, err);
            return path;
        }

        private FileItem ItemFor(string downloadPath, int seconds = 3) => new()
        {
            MediaKey = Path.GetFileName(downloadPath),
            Duration = TimeSpan.FromSeconds(seconds),
            Size = new FileInfo(downloadPath).Length,
            DownloadPath = downloadPath,
        };

        private string CompressDir => Path.Combine(_dir, "out");

        [SkippableFact]
        public void Compresses_and_verifies_clip()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            FileItem item = ItemFor(MakeClip("clip.mp4"));

            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(ffmpeg, item, CompressDir));

            Assert.True(item.Verified);
            Assert.True(File.Exists(item.CompressPath));
            Assert.True(new FileInfo(item.CompressPath).Length < item.Size);
            Assert.Empty(Directory.GetFiles(CompressDir, "*.part*"));
        }

        [SkippableFact]
        public void Keeps_creation_time()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            FileItem item = ItemFor(MakeClip("clip.mp4"));

            Compressor.CompressOne(ffmpeg, item, CompressDir);

            (_, string info) = ffmpeg.Run(new[] { "-hide_banner", "-i", item.CompressPath });
            Assert.Contains("creation_time   : 2024-05-01T10:00:00", info);
        }

        [SkippableFact]
        public void Handles_size_that_halves_to_odd_numbers()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            // 642x362 halves to 321x181, which libx264 rejects without rounding.
            FileItem item = ItemFor(MakeClip("odd.mp4", size: "642x362"));

            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(ffmpeg, item, CompressDir));
        }

        [SkippableFact]
        public void Works_for_mov()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            FileItem item = ItemFor(MakeClip("clip.MOV"));

            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(ffmpeg, item, CompressDir));
            Assert.EndsWith(".MOV", item.CompressPath);
        }

        [SkippableFact]
        public void Skips_video_that_would_not_get_smaller()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            // Already squeezed to the worst quality; re-encoding at CRF 20 makes it bigger, not smaller.
            FileItem item = ItemFor(MakeClip("squeezed.mp4", crf: 51));

            Assert.Equal(CompressResult.NotWorthIt, Compressor.CompressOne(ffmpeg, item, CompressDir));

            Assert.True(item.CompressionSkipped);
            Assert.False(item.Verified);
            Assert.Null(item.CompressPath);
            Assert.Empty(Directory.GetFiles(CompressDir));
        }

        [SkippableFact]
        public void Cut_off_copy_from_earlier_run_is_redone()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            FileItem item = ItemFor(MakeClip("clip.mp4", seconds: 4), seconds: 4);
            item.CompressPath = MakeClip("clip_compressed.mp4", seconds: 1, size: "320x180", crf: 40); // like a crash mid-encode

            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(ffmpeg, item, CompressDir));

            Assert.True(Compressor.IsDurationClose(TimeSpan.FromSeconds(4), ffmpeg.GetDuration(item.CompressPath)!.Value));
            Assert.False(File.Exists(Path.Combine(_dir, "clip_compressed.mp4")));
        }

        [SkippableFact]
        public void Good_copy_from_earlier_run_is_accepted_without_reencoding()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            FileItem item = ItemFor(MakeClip("clip.mp4"));
            Compressor.CompressOne(ffmpeg, item, CompressDir);
            DateTime written = File.GetLastWriteTimeUtc(item.CompressPath);
            item.Verified = false;

            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(ffmpeg, item, CompressDir));
            Assert.Equal(written, File.GetLastWriteTimeUtc(item.CompressPath));
        }

        [SkippableFact]
        public void Unsupported_format_is_skipped()
        {
            Ffmpeg ffmpeg = RequireFfmpeg();
            string path = Path.Combine(_dir, "clip.avi");
            File.WriteAllText(path, "x");

            Assert.Equal(CompressResult.NotWorthIt, Compressor.CompressOne(ffmpeg, ItemFor(path), CompressDir));
        }

        [Fact]
        public void Missing_source_counts_as_failure()
        {
            var item = new FileItem { MediaKey = "k", DownloadPath = Path.Combine(_dir, "gone.mp4"), Duration = TimeSpan.FromSeconds(3) };

            Assert.Equal(CompressResult.Failed, Compressor.CompressOne(new Ffmpeg("unused"), item, CompressDir));
            Assert.Equal(1, item.FailCount);
        }

        [Theory]
        [InlineData(10, 10.4, true)]
        [InlineData(10, 9.1, true)]
        [InlineData(10, 8.5, false)]
        [InlineData(300, 295, true)]
        [InlineData(300, 290, false)]
        public void Duration_tolerance(double expected, double actual, bool close)
        {
            Assert.Equal(close, Compressor.IsDurationClose(TimeSpan.FromSeconds(expected), TimeSpan.FromSeconds(actual)));
        }

        [Fact]
        public void Audio_fallback_args_reencode_audio()
        {
            Assert.Contains("copy", Compressor.BuildArgs("in.mp4", "out.mp4", copyAudio: true));
            Assert.Contains("aac", Compressor.BuildArgs("in.mp4", "out.mp4", copyAudio: false));
        }
    }

    public class FfmpegTests
    {
        [Fact]
        public void ParseDuration_reads_ffmpeg_output()
        {
            const string output = "Input #0, mov,mp4 from 'x.mp4':\n  Duration: 00:01:05.53, start: 0.000000, bitrate: 2 kb/s";

            Assert.Equal(TimeSpan.FromSeconds(65.53), Ffmpeg.ParseDuration(output));
        }

        [Fact]
        public void ParseDuration_returns_null_without_duration()
        {
            Assert.Null(Ffmpeg.ParseDuration("x.mp4: Invalid data found when processing input"));
            Assert.Null(Ffmpeg.ParseDuration("Duration: N/A, bitrate: N/A"));
        }
    }
}
