using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class SettingsTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-settings-").FullName;

        public void Dispose() => Directory.Delete(_dir, true);

        [Fact]
        public void Defaults_match_the_values_the_app_always_used()
        {
            var s = new Settings();

            Assert.Equal(10, s.ItemsPerBatch);
            Assert.Equal(0.8 * 1024 * 1024, s.MinVideoBytesPerSecond);
            Assert.Equal(2L * 1024 * 1024, s.MinPhotoBytes);
            Assert.Equal(16_000_000, s.MaxPhotoPixels);
            Assert.Equal(0.9, s.MaxCompressedSizeRatio, 6);
            Assert.Equal(3, s.MaxFails);
            Assert.True(s.ConfirmBeforeBin);
            Assert.False(s.DeleteLocalFilesWhenDone);
            Assert.Equal(Const.APP_DOWNLOADS_PATH, s.WorkFolderOrDefault);
        }

        [Fact]
        public void App_folders_are_absolute_paths()
        {
            // On a fresh Linux account ~/.local/share may not exist; the folder must still not be relative.
            Assert.True(Path.IsPathRooted(Settings.CONFIG_DIR), Settings.CONFIG_DIR);
            Assert.True(Path.IsPathRooted(FfmpegInstaller.INSTALL_DIR), FfmpegInstaller.INSTALL_DIR);
            Assert.True(Path.IsPathRooted(GooglePhotosAutoCompressor.Modules.Initlizer.GetProfileDir()));
        }

        [Fact]
        public void Saves_and_loads_every_value()
        {
            string path = Path.Combine(_dir, "settings.json");
            var s = new Settings
            {
                CompressPhotos = false,
                VideoResolution = VideoResolution.Max720p,
                VideoCrf = 24,
                ItemsPerBatch = 25,
                RepeatUntilDone = true,
                WorkFolder = _dir,
            };

            s.Save(path);
            Settings loaded = Settings.Load(path);

            Assert.False(loaded.CompressPhotos);
            Assert.Equal(VideoResolution.Max720p, loaded.VideoResolution);
            Assert.Equal(24, loaded.VideoCrf);
            Assert.Equal(25, loaded.ItemsPerBatch);
            Assert.True(loaded.RepeatUntilDone);
            Assert.Equal(_dir, loaded.WorkFolderOrDefault);
            Assert.Contains("\"Max720p\"", File.ReadAllText(path)); // readable in the file
        }

        [Fact]
        public void Missing_or_broken_file_gives_defaults()
        {
            Assert.Equal(10, Settings.Load(Path.Combine(_dir, "none.json")).ItemsPerBatch);

            string broken = Path.Combine(_dir, "broken.json");
            File.WriteAllText(broken, "{ not json");
            Assert.Equal(10, Settings.Load(broken).ItemsPerBatch);
        }

        [Fact]
        public void Out_of_range_values_are_brought_into_range()
        {
            Settings s = new Settings { ItemsPerBatch = 0, VideoCrf = 99, PhotoJpegQuality = 5, MinSavingPercent = 100, ParallelDownloads = 50, WorkFolder = "  " }.Normalized();

            Assert.Equal(1, s.ItemsPerBatch);
            Assert.Equal(51, s.VideoCrf);
            Assert.Equal(30, s.PhotoJpegQuality);
            Assert.Equal(90, s.MinSavingPercent);
            Assert.Equal(10, s.ParallelDownloads);
            Assert.Equal(Const.APP_DOWNLOADS_PATH, s.WorkFolderOrDefault);
        }

        [Fact]
        public void Turning_off_videos_or_photos_leaves_them_alone()
        {
            var video = new FileItem { MediaKey = "v", Duration = TimeSpan.FromSeconds(10), Size = 50L * 1024 * 1024 };
            var photo = new FileItem { MediaKey = "p", MediaType = MediaType.Image, Size = 10L * 1024 * 1024 };

            Assert.True(ItemRules.IsCompressionReasonable(video, new Settings()));
            Assert.False(ItemRules.IsCompressionReasonable(video, new Settings { CompressVideos = false }));
            Assert.True(ItemRules.IsCompressionReasonable(photo, new Settings()));
            Assert.False(ItemRules.IsCompressionReasonable(photo, new Settings { CompressPhotos = false }));
        }

        [Fact]
        public void Thresholds_come_from_settings()
        {
            var video = new FileItem { MediaKey = "v", Duration = TimeSpan.FromSeconds(10), Size = 15L * 1024 * 1024 }; // 1.5 MB/s

            Assert.True(ItemRules.IsCompressionReasonable(video, new Settings { MinVideoMbPerSecond = 1 }));
            Assert.False(ItemRules.IsCompressionReasonable(video, new Settings { MinVideoMbPerSecond = 2 }));
        }

        [Fact]
        public void Video_quality_settings_reach_ffmpeg()
        {
            List<string> args = Compressor.BuildArgs("in.mp4", "out.mp4", copyAudio: true, VideoResolution.Max720p, crf: 26).ToList();

            Assert.Equal("26", args[args.IndexOf("-crf") + 1]);
            Assert.Contains("720", args[args.IndexOf("-vf") + 1]);
        }

        [Fact]
        public void Default_video_args_are_unchanged()
        {
            List<string> args = Compressor.BuildArgs("in.mp4", "out.mp4", copyAudio: true).ToList();

            Assert.Equal("scale=trunc(iw/4)*2:trunc(ih/4)*2", args[args.IndexOf("-vf") + 1]);
            Assert.Equal("20", args[args.IndexOf("-crf") + 1]);
        }

        [Fact]
        public void Reads_ffmpeg_progress_time()
        {
            Assert.Equal(TimeSpan.FromSeconds(62.5), Ffmpeg.ParseProgressTime("frame=  100 fps= 30 q=28.0 size=    256kB time=00:01:02.50 bitrate= 33.5kbits/s"));
            Assert.Null(Ffmpeg.ParseProgressTime("Input #0, mov"));
        }
    }

    public sealed class ItemStatusTests
    {
        private static FileItem Video() => new() { MediaKey = Guid.NewGuid().ToString(), Duration = TimeSpan.FromSeconds(10), Size = 50L * 1024 * 1024 };

        [Fact]
        public void Each_stage_has_its_own_status()
        {
            FileItem item = Video();
            Assert.Equal(ItemStatus.Waiting, ItemRules.Describe(item));

            item.Verified = true;
            Assert.Equal(ItemStatus.Compressed, ItemRules.Describe(item));

            item.OriginalDeleted = true;
            Assert.Equal(ItemStatus.InBin, ItemRules.Describe(item));

            item.CompressUploaded = true;
            Assert.Equal(ItemStatus.Uploaded, ItemRules.Describe(item));

            item.DateSet = true;
            Assert.Equal(ItemStatus.Done, ItemRules.Describe(item));
            Assert.True(ItemRules.IsDone(item));
        }

        [Fact]
        public void Small_skipped_and_failed_items_are_told_apart()
        {
            Assert.Equal(ItemStatus.Small, ItemRules.Describe(new FileItem { MediaKey = "s", Duration = TimeSpan.FromSeconds(10), Size = 1024 }));
            Assert.Equal(ItemStatus.Skipped, ItemRules.Describe(new FileItem { MediaKey = "k", CompressionSkipped = true }));

            FileItem failed = Video();
            failed.FailCount = Settings.Current.MaxFails;
            Assert.Equal(ItemStatus.Failed, ItemRules.Describe(failed));
        }

        [Fact]
        public void Original_in_bin_is_never_shown_as_failed()
        {
            // The copy still has to be uploaded; hiding that behind "failed" would lose track of it.
            FileItem item = Video();
            item.OriginalDeleted = true;
            item.FailCount = 99;

            Assert.Equal(ItemStatus.InBin, ItemRules.Describe(item));
        }
    }
}
