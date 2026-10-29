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

        private static FileItem Photo(double mb) => new()
        {
            MediaType = MediaType.Image,
            MediaKey = Guid.NewGuid().ToString(),
            Size = (long)(mb * 1024 * 1024),
        };

        [Theory]
        [InlineData(1.0, false)]
        [InlineData(2.0, true)]
        [InlineData(12.0, true)]
        public void Photo_compression_threshold_is_by_size(double mb, bool expected)
        {
            Assert.Equal(expected, ItemRules.IsCompressionReasonable(Photo(mb)));
        }

        [Fact]
        public void New_photo_needs_download_and_counts_in_batch()
        {
            FileItem photo = Photo(5);

            Assert.True(ItemRules.NeedsDownload(photo));
            Assert.True(ItemRules.IsInProgress(photo));
        }

        [Fact]
        public void Photo_copy_never_needs_the_date_step()
        {
            // EXIF date taken is the original's, so Google places the copy at the right date and time by itself.
            FileItem photo = Photo(5);
            photo.OriginalDeleted = true;
            photo.Verified = true;
            photo.CompressUploaded = true;
            photo.CompressPath = ExistingFile("p_compressed_1234abcd.jpg");
            Assert.False(ItemRules.NeedsDateSet(photo));

            photo.TakenAt = new DateTime(2025, 7, 4, 17, 57, 0);
            Assert.False(ItemRules.NeedsDateSet(photo));
        }

        [Fact]
        public void Original_goes_to_bin_only_after_its_time_was_read()
        {
            FileItem item = Video();
            item.Verified = true;
            item.CompressPath = ExistingFile("v_compressed_1234abcd.mp4");

            Assert.True(ItemRules.CanDeleteOriginal(item));
            Assert.True(ItemRules.NeedsTakenTime(item));
            Assert.False(ItemRules.IsReadyForBin(item)); // the time would be lost with the original

            item.TakenAt = new DateTime(2025, 7, 4, 17, 57, 0);
            Assert.False(ItemRules.NeedsTakenTime(item));
            Assert.True(ItemRules.IsReadyForBin(item));
        }

        [Fact]
        public void Original_goes_to_bin_without_time_after_repeated_read_failures()
        {
            FileItem item = Video();
            item.Verified = true;
            item.CompressPath = ExistingFile("v_compressed_1234abcd.mp4");

            item.TakenReadMisses = TakenTime.READ_TRIES - 1;
            Assert.False(ItemRules.IsReadyForBin(item));

            item.TakenReadMisses = TakenTime.READ_TRIES;
            Assert.False(ItemRules.NeedsTakenTime(item));
            Assert.True(ItemRules.IsReadyForBin(item)); // the copy keeps the time stored in the file
        }

        [Fact]
        public void Time_is_not_read_for_a_copy_that_is_already_dated()
        {
            // Older versions uploaded and dated copies but never binned the original (seen in the owner's state:
            // 12 items). Their originals did not open in the viewer, and the time would not be used anyway.
            FileItem item = Video();
            item.CompressUploaded = true;
            item.DateSet = true;

            Assert.True(ItemRules.CanDeleteOriginal(item));
            Assert.False(ItemRules.NeedsTakenTime(item));
            Assert.True(ItemRules.IsReadyForBin(item)); // the old rule: bin it (or mark it binned if gone)
        }

        [Fact]
        public void Time_is_never_read_without_a_good_copy()
        {
            FileItem item = Video();
            Assert.False(ItemRules.NeedsTakenTime(item));
            Assert.False(ItemRules.IsReadyForBin(item));

            item.OriginalDeleted = true;
            Assert.False(ItemRules.NeedsTakenTime(item));
        }

        [Fact]
        public void Photo_with_date_set_at_upload_is_finished()
        {
            FileItem photo = Photo(5);
            photo.OriginalDeleted = true;
            photo.CompressUploaded = true;
            photo.DateSet = true; // what the upload step sets for photo copies

            Assert.False(ItemRules.IsInProgress(photo));
            Assert.False(ItemRules.NeedsDownload(photo));
        }

        [Fact]
        public void Same_data_safety_rules_apply_to_photos()
        {
            FileItem photo = Photo(5);
            photo.CompressPath = ExistingFile("p_compressed_1234abcd.jpg");

            Assert.False(ItemRules.CanDeleteOriginal(photo)); // not verified yet
            Assert.False(ItemRules.CanUpload(photo));

            photo.Verified = true;
            Assert.True(ItemRules.CanDeleteOriginal(photo));
            Assert.False(ItemRules.CanUpload(photo)); // original not binned yet

            photo.OriginalDeleted = true;
            Assert.True(ItemRules.CanUpload(photo));
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
            failed.FailCount = Settings.Current.MaxFails;

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
