namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class DownloadCheckTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-download-check-").FullName;

        public void Dispose() => Directory.Delete(_dir, true);

        /// <summary>A tile showing "<paramref name="shownMb"/> MB", as the tile parser stores it (1 MB = 1,048,576).</summary>
        private static FileItem Tile(double shownMb, int seconds = 0) => new()
        {
            MediaKey = "k",
            Size = (long)(shownMb * 1024 * 1024),
            Duration = TimeSpan.FromSeconds(seconds),
        };

        [Theory]
        // Real pairs from the owner's downloads (2026-11-10): Google's list shows decimal MB.
        [InlineData(12.6, 12_550_000)]   // video, −0.4 %
        [InlineData(8.4, 8_199_000)]     // photo, −2.4 % (the furthest seen)
        [InlineData(8.5, 8_540_000)]     // photo, +0.5 %
        public void Complete_downloads_pass(double shownMb, long actualBytes)
        {
            Assert.Null(DownloadCheck.SizeProblem(Tile(shownMb), actualBytes));
        }

        [Theory]
        [InlineData(94.3, 40_000_000)]   // cut off half way
        [InlineData(8.5, 7_000_000)]     // 18 % missing
        [InlineData(8.5, 12_000_000)]    // some other, bigger file
        public void Cut_off_or_wrong_downloads_fail(double shownMb, long actualBytes)
        {
            Assert.NotNull(DownloadCheck.SizeProblem(Tile(shownMb), actualBytes));
        }

        [Fact]
        public void A_stalled_download_has_a_time_limit_that_grows_with_the_file()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), DownloadCheck.TimeLimit(Tile(8.5).Size));     // small: the minimum
            Assert.Equal(1000, DownloadCheck.TimeLimit(Tile(100).Size).TotalSeconds, 0);          // 100 MB at 100 KB/s
            Assert.True(DownloadCheck.TimeLimit(Tile(2000).Size) > TimeSpan.FromHours(5));        // 2 GB
            Assert.Equal(TimeSpan.FromMinutes(5), DownloadCheck.TimeLimit(0));                    // size unknown
        }

        [Fact]
        public void Video_length_must_match_the_list()
        {
            FileItem item = Tile(50, seconds: 28);

            Assert.Null(DownloadCheck.DurationProblem(item, TimeSpan.FromSeconds(28.4)));   // the list rounds to seconds
            Assert.Null(DownloadCheck.DurationProblem(item, TimeSpan.FromSeconds(27.1)));
            Assert.NotNull(DownloadCheck.DurationProblem(item, TimeSpan.FromSeconds(14)));  // plays only half
            Assert.NotNull(DownloadCheck.DurationProblem(item, null));                      // ffmpeg can't read it
            Assert.Null(DownloadCheck.DurationProblem(Tile(50), TimeSpan.FromSeconds(3)));  // no listed length: size decides
        }

        [Fact]
        public void A_broken_download_is_deleted_and_counts_as_an_attempt()
        {
            string folder = Path.Combine(_dir, FilePaths.ShortId("k")); // the item's own download folder
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "VID.mp4");
            File.WriteAllText(path, "half");
            FileItem item = new() { MediaKey = "k", DownloadPath = path };

            DownloadCheck.Discard(item, "test");

            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(folder));
            Assert.Null(item.DownloadPath);
            Assert.Equal(1, item.FailCount);
            Assert.True(ItemRules.NeedsDownload(item)); // so the next run downloads it again
        }

        [Fact]
        public void Only_the_items_own_folder_is_removed()
        {
            string path = Path.Combine(_dir, "VID.mp4"); // not in a per-item folder
            File.WriteAllText(path, "half");

            DownloadCheck.Discard(new FileItem { MediaKey = "k", DownloadPath = path }, "test");

            Assert.True(Directory.Exists(_dir));
        }
    }
}
