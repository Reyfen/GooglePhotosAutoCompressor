using GooglePhotosAutoCompressor.Modules;
using Xunit.Abstractions;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>
    /// AI image tests: the local Claude Code CLI looks at frames from the original and compressed videos.
    /// They need ffmpeg and Claude Code, cost a few cents and take about 15 s each.
    /// Leave them out with: dotnet test --filter "Category!=AI"
    /// </summary>
    [Trait("Category", "AI")]
    public sealed class CompressorVisionTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-vision-").FullName;
        private readonly Ffmpeg? _ffmpeg = Ffmpeg.FindExecutable() is { } path ? new Ffmpeg(path) : null;

        public CompressorVisionTests(ITestOutputHelper output) => _out = output;

        public void Dispose() => Directory.Delete(_dir, true);

        private Ffmpeg RequireTools()
        {
            if (ClaudeCode.ExecutablePath == null) _out.WriteLine(ClaudeCode.MISSING_MESSAGE);
            Skip.If(ClaudeCode.ExecutablePath == null, ClaudeCode.MISSING_MESSAGE);
            Skip.If(_ffmpeg == null, "AI image test skipped: ffmpeg not found. Install ffmpeg or put it on PATH.");
            return _ffmpeg!;
        }

        private string MakeClip(string name, string size)
        {
            string path = Path.Combine(_dir, name);
            (int exit, string err) = _ffmpeg!.Run(new[]
            {
                "-hide_banner", "-y",
                "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=30:duration=3",
                "-c:v", "libx264", "-crf", "5", "-pix_fmt", "yuv444p",
                path,
            });
            Assert.True(exit == 0, err);
            return path;
        }

        private string Frame(string video, string name, string? filter = null)
        {
            string png = Path.Combine(_dir, name);
            var args = new List<string> { "-hide_banner", "-y", "-ss", "1.5", "-i", video, "-frames:v", "1" };
            if (filter != null) args.AddRange(new[] { "-vf", filter });
            args.Add(png);
            (int exit, string err) = _ffmpeg!.Run(args.ToArray());
            Assert.True(exit == 0, err);
            return png;
        }

        private string Compress(Ffmpeg ffmpeg, string clip)
        {
            var item = new FileItem
            {
                MediaKey = Path.GetFileName(clip),
                Duration = TimeSpan.FromSeconds(3),
                Size = new FileInfo(clip).Length,
                DownloadPath = clip,
            };
            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(ffmpeg, item, Path.Combine(_dir, "out")));
            return item.CompressPath!;
        }

        private FrameVerdict Judge(string original, string candidate)
        {
            FrameVerdict v = ClaudeCode.CompareFrames(original, candidate);
            _out.WriteLine($"Claude Code ({ClaudeCode.Model}): same_scene={v.SameScene}, corrupted={v.Corrupted}, reason: {v.Reason}");
            return v;
        }

        [SkippableTheory]
        [InlineData("1280x720")]   // landscape
        [InlineData("362x642")]    // portrait, halves to odd numbers
        public void Compressed_copy_looks_like_the_original(string size)
        {
            Ffmpeg ffmpeg = RequireTools();
            string clip = MakeClip("clip.mp4", size);
            string compressed = Compress(ffmpeg, clip);

            FrameVerdict v = Judge(Frame(clip, "original.png"), Frame(compressed, "compressed.png"));

            Assert.True(v.SameScene, $"Claude Code says the compressed frame shows a different picture: {v.Reason}");
            Assert.False(v.Corrupted, $"Claude Code says the compressed frame is damaged: {v.Reason}");
        }

        /// <summary>Control: proves the AI check fails on damaged output instead of always passing.</summary>
        [SkippableFact]
        public void Spots_a_damaged_frame()
        {
            Ffmpeg ffmpeg = RequireTools();
            string clip = MakeClip("clip.mp4", "1280x720");
            string compressed = Compress(ffmpeg, clip);

            // Typical decoder damage: a big green block plus a smeared lower half.
            string damage = "drawbox=x=0:y=0:w=iw*0.6:h=ih*0.4:color=0x00FF00:t=fill," +
                            "split[a][b];[b]crop=iw:ih/2:0:ih/2,boxblur=20[low];[a][low]overlay=0:H/2";
            FrameVerdict v = Judge(Frame(clip, "original.png"), Frame(compressed, "damaged.png", damage));

            Assert.True(v.Corrupted, $"Claude Code missed obvious damage: {v.Reason}");
        }

        /// <summary>Control: a different video must not count as the same picture.</summary>
        [SkippableFact]
        public void Spots_a_different_picture()
        {
            Ffmpeg ffmpeg = RequireTools();
            string clip = MakeClip("clip.mp4", "1280x720");
            string other = Path.Combine(_dir, "other.png");
            (int exit, string err) = ffmpeg.Run(new[]
            {
                "-hide_banner", "-y", "-f", "lavfi", "-i", "smptebars=size=640x360", "-frames:v", "1", other,
            });
            Assert.True(exit == 0, err);

            FrameVerdict v = Judge(Frame(clip, "original.png"), other);

            Assert.False(v.SameScene, $"Claude Code thinks a different video is the same picture: {v.Reason}");
        }
    }
}
