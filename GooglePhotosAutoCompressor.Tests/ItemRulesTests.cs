namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class ItemRulesTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-rules-").FullName;

        public void Dispose() => Directory.Delete(_dir, true);

        private string ExistingFile(string name)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, "x");
            return path;
        }

        private static FileItem Video(double mbPerSec = 2, int seconds = 10) => new()
        {
            MediaKey = Guid.NewGuid().ToString(),
            Duration = TimeSpan.FromSeconds(seconds),
            Size = (long)(mbPerSec * 1024 * 1024 * seconds),
        };

        [Theory]
        [InlineData(0.5, false)]
        [InlineData(0.8, true)]
        [InlineData(3.0, true)]
        public void Compression_threshold(double mbPerSec, bool expected)
        {
            Assert.Equal(expected, ItemRules.IsCompressionReasonable(Video(mbPerSec)));
        }

        [Fact]
        public void Zero_duration_is_not_reasonable()
        {
            Assert.False(ItemRules.IsCompressionReasonable(Video(seconds: 0)));
        }

        [Fact]
        public void New_item_needs_download()
        {
            Assert.True(ItemRules.NeedsDownload(Video()));
        }

        [Fact]
        public void Item_with_file_on_disk_or_finished_does_not_need_download()
        {
            Assert.False(ItemRules.NeedsDownload(new FileItem { MediaKey = "a", DownloadPath = ExistingFile("a.mp4") }));
            Assert.False(ItemRules.NeedsDownload(new FileItem { MediaKey = "b", CompressPath = ExistingFile("b.mp4") }));
            Assert.False(ItemRules.NeedsDownload(new FileItem { MediaKey = "c", OriginalDeleted = true }));
            Assert.False(ItemRules.NeedsDownload(new FileItem { MediaKey = "d", CompressUploaded = true }));
        }

        [Fact]
        public void Unverified_copy_never_allows_delete()
        {
            FileItem item = Video();
            item.CompressPath = ExistingFile("c.mp4");

            Assert.False(ItemRules.CanDeleteOriginal(item));
        }

        [Fact]
        public void Verified_copy_missing_from_disk_does_not_allow_delete()
        {
            FileItem item = Video();
            item.CompressPath = Path.Combine(_dir, "gone.mp4");
            item.Verified = true;

            Assert.False(ItemRules.CanDeleteOriginal(item));
        }

        [Fact]
        public void Verified_copy_on_disk_allows_delete()
        {
            FileItem item = Video();
            item.CompressPath = ExistingFile("c.mp4");
            item.Verified = true;

            Assert.True(ItemRules.CanDeleteOriginal(item));
        }

        [Fact]
        public void Uploaded_but_original_kept_is_deleted_next_run()
        {
            // The 12 duplicates in the owner's state file: copy uploaded, cache folder gone, original kept.
            var item = new FileItem
            {
                MediaKey = "dup",
                CompressPath = Path.Combine(_dir, "deleted-cache", "x_compressed.mp4"),
                CompressUploaded = true,
                DateSet = true,
            };

            Assert.True(ItemRules.CanDeleteOriginal(item));
        }

        [Fact]
        public void Upload_waits_for_original_in_bin()
        {
            FileItem item = Video();
            item.CompressPath = ExistingFile("c.mp4");
            item.Verified = true;

            Assert.False(ItemRules.CanUpload(item));

            item.OriginalDeleted = true;
            Assert.True(ItemRules.CanUpload(item));

            item.CompressUploaded = true;
            Assert.False(ItemRules.CanUpload(item));
        }

        [Fact]
        public void Upload_requires_verified_copy()
        {
            FileItem item = Video();
            item.CompressPath = ExistingFile("c.mp4");
            item.OriginalDeleted = true;

            Assert.False(ItemRules.CanUpload(item));
        }

        [Fact]
        public void Skipped_or_failed_items_are_left_alone()
        {
            FileItem skipped = Video();
            skipped.CompressionSkipped = true;
            FileItem failed = Video();
            failed.FailCount = Const.MAX_FAILS;

            foreach (FileItem item in new[] { skipped, failed })
            {
                item.DownloadPath = ExistingFile(item.MediaKey + ".mp4");
                Assert.False(ItemRules.NeedsDownload(item));
                Assert.False(ItemRules.NeedsCompression(item));
                Assert.False(ItemRules.IsInProgress(item));
            }
        }

        [Fact]
        public void Downloaded_or_unchecked_copy_needs_compression()
        {
            FileItem downloaded = Video();
            downloaded.DownloadPath = ExistingFile("d.mp4");
            Assert.True(ItemRules.NeedsCompression(downloaded));

            FileItem unchecked_ = Video();
            unchecked_.CompressPath = ExistingFile("c.mp4");
            Assert.True(ItemRules.NeedsCompression(unchecked_));

            unchecked_.Verified = true;
            Assert.False(ItemRules.NeedsCompression(unchecked_));
        }

        [Fact]
        public void Low_bitrate_and_finished_items_are_not_in_progress()
        {
            Assert.True(ItemRules.IsInProgress(Video(2)));
            Assert.False(ItemRules.IsInProgress(Video(0.3)));

            FileItem done = Video(2);
            done.DateSet = true;
            Assert.False(ItemRules.IsInProgress(done));
        }
    }
}
