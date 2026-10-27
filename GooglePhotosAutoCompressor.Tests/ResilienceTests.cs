using GooglePhotosAutoCompressor.Modules;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>What the next run finds after the app was killed, the power went out or the internet dropped.</summary>
    public sealed class ResilienceTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-resume-").FullName;

        public void Dispose() => Directory.Delete(_dir, true);

        private string WriteFile(string relative, string content = "x")
        {
            string path = Path.Combine(_dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void Durable_write_leaves_no_temp_file_and_keeps_a_backup()
        {
            string path = Path.Combine(_dir, "state.json");
            string backup = path + ".bak";

            DurableFile.WriteAllText(path, "one", backup);
            DurableFile.WriteAllText(path, "two", backup);

            Assert.Equal("two", File.ReadAllText(path));
            Assert.Equal("one", File.ReadAllText(backup));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Empty_state_file_after_power_cut_falls_back_to_backup()
        {
            string path = Path.Combine(_dir, "state.json");
            var data = new FileItemsData(path);
            data.AddOrGet(new FileItem { MediaKey = "a" });
            data.Save();
            data.AddOrGet(new FileItem { MediaKey = "b" });
            data.Save(); // backup now holds "a"

            File.WriteAllText(path, ""); // what a power cut can leave

            var loaded = new FileItemsData(path);
            Assert.Contains(loaded.Items, i => i.MediaKey == "a");
        }

        [Fact]
        public void Empty_state_file_without_backup_is_not_read_as_no_items()
        {
            // Starting with an empty list would overwrite the user's progress on the next save.
            string path = WriteFile("state.json", "");

            Assert.Throws<InvalidDataException>(() => new FileItemsData(path));
        }

        [Fact]
        public void Saved_event_fires_after_each_save()
        {
            var data = new FileItemsData(Path.Combine(_dir, "state.json"));
            int saves = 0;
            data.Saved += () => saves++;

            data.Save();
            data.Save();

            Assert.Equal(2, saves);
        }

        [Theory]
        [InlineData("IMG_1.MOV.part", true)]
        [InlineData("IMG_1_compressed_1a2b3c4d.part.MOV", true)]
        [InlineData("IMG_1_compressed_1a2b3c4d.part.jpg", true)]
        [InlineData("IMG_1.MOV", false)]
        [InlineData("IMG_1_compressed_1a2b3c4d.MOV", false)]
        [InlineData("my.party.mp4", false)]
        [InlineData("holiday.part.mp4", false)] // a user's own file name, not ours
        public void Recognises_half_written_files(string name, bool expected)
        {
            Assert.Equal(expected, FilePaths.IsPartialFile(name));
        }

        [Fact]
        public void Deletes_half_written_files_and_keeps_finished_ones()
        {
            string partialDownload = WriteFile("dl/1a2b3c4d/IMG_1.MOV.part");
            string download = WriteFile("dl/1a2b3c4d/IMG_1.MOV");
            string partialCopy = WriteFile("out/IMG_1_compressed_1a2b3c4d.part.MOV");
            string copy = WriteFile("out/IMG_2_compressed_5e6f7a8b.MOV");

            int deleted = FilePaths.DeletePartialFiles(Path.Combine(_dir, "dl"), Path.Combine(_dir, "out"), Path.Combine(_dir, "missing"));

            Assert.Equal(2, deleted);
            Assert.False(File.Exists(partialDownload));
            Assert.False(File.Exists(partialCopy));
            Assert.True(File.Exists(download));
            Assert.True(File.Exists(copy));
        }

        [Fact]
        public void Finished_copy_not_yet_recorded_is_picked_up()
        {
            // Killed after the copy was renamed into place, before the state file was saved.
            string download = WriteFile("dl/IMG_1.MOV");
            string outDir = Path.Combine(_dir, "out");
            string copy = FilePaths.CompressPath(outDir, "key", download);
            WriteFile(Path.GetRelativePath(_dir, copy));
            var item = new FileItem { MediaKey = "key", DownloadPath = download };

            Compressor.AdoptFinishedCopy(item, outDir);

            Assert.Equal(copy, item.CompressPath);
        }

        [Fact]
        public void Recorded_copy_is_not_replaced_by_a_guess()
        {
            string download = WriteFile("dl/IMG_1.MOV");
            var item = new FileItem { MediaKey = "key", DownloadPath = download, CompressPath = "recorded.mp4" };

            Compressor.AdoptFinishedCopy(item, Path.Combine(_dir, "out"));

            Assert.Equal("recorded.mp4", item.CompressPath);
        }

        [Theory]
        [InlineData(100, 1000, 500, true)]  // 1000 - 200 >= 500
        [InlineData(300, 1000, 500, false)] // 1000 - 600 < 500
        [InlineData(0, 499, 500, false)]
        public void Disk_space_check_leaves_room_for_download_and_copy(long size, long free, long minFree, bool expected)
        {
            Assert.Equal(expected, DiskSpace.HasRoomFor(size, free, minFree));
        }

        [Fact]
        public void Free_space_of_an_existing_folder_is_known()
        {
            long free = DiskSpace.FreeBytes(_dir);

            Assert.True(free > 0 && free < long.MaxValue);
        }

        [Fact]
        public void Copy_not_found_three_runs_in_a_row_is_uploaded_again()
        {
            string copy = WriteFile("out/IMG_1_compressed_1a2b3c4d.MOV");
            var item = new FileItem { MediaKey = "k", CompressPath = copy, Verified = true, OriginalDeleted = true, CompressUploaded = true };

            DateSetter.CountMiss(item);
            DateSetter.CountMiss(item);
            Assert.True(item.CompressUploaded);
            Assert.False(ItemRules.CanUpload(item));

            DateSetter.CountMiss(item);

            Assert.False(item.CompressUploaded);
            Assert.True(ItemRules.CanUpload(item));
            Assert.Equal(0, item.DateSetMisses);
        }

        [Fact]
        public void Copy_missing_on_disk_is_not_marked_for_upload_again()
        {
            var item = new FileItem { MediaKey = "k", CompressPath = Path.Combine(_dir, "gone.MOV"), OriginalDeleted = true, CompressUploaded = true };

            for (int i = 0; i < 5; i++)
            {
                DateSetter.CountMiss(item);
            }

            Assert.True(item.CompressUploaded);
        }

        [Theory]
        [InlineData("x_compressed_1a2b3c4d.jpg", true)]
        [InlineData("x_compressed_1a2b3c4d.mp4", false)]
        public void Upload_marks_photo_copies_finished_and_videos_waiting_for_date(string name, bool dateSet)
        {
            var item = new FileItem { MediaKey = "k", CompressPath = name };

            Uploader.MarkUploaded(item);

            Assert.True(item.CompressUploaded);
            Assert.Equal(dateSet, item.DateSet);
        }

        [Fact]
        public void Second_copy_of_the_app_cannot_take_the_lock()
        {
            string path = Path.Combine(_dir, "app.lock");

            using (AppLock? first = AppLock.TryAcquire(path))
            {
                Assert.NotNull(first);
                Assert.Null(AppLock.TryAcquire(path));
            }

            using AppLock? again = AppLock.TryAcquire(path);
            Assert.NotNull(again);
        }

        [Fact]
        public void Local_files_are_deleted_only_for_finished_items()
        {
            string doneDownload = WriteFile("dl/aaaa/done.MOV");
            string doneCopy = WriteFile("out/done_compressed_aaaaaaaa.MOV");
            string busyDownload = WriteFile("dl/bbbb/busy.MOV");
            string busyCopy = WriteFile("out/busy_compressed_bbbbbbbb.MOV");

            var data = new FileItemsData(Path.Combine(_dir, "state.json"));
            data.AddOrGet(new FileItem { MediaKey = "done", DownloadPath = doneDownload, CompressPath = doneCopy, OriginalDeleted = true, CompressUploaded = true, DateSet = true });
            data.AddOrGet(new FileItem { MediaKey = "busy", DownloadPath = busyDownload, CompressPath = busyCopy, OriginalDeleted = true, Verified = true });

            long freed = LocalFiles.DeleteFinished(data);

            Assert.Equal(2, freed);
            Assert.False(File.Exists(doneDownload));
            Assert.False(Directory.Exists(Path.GetDirectoryName(doneDownload)));
            Assert.False(File.Exists(doneCopy));
            Assert.True(File.Exists(busyDownload));
            Assert.True(File.Exists(busyCopy));
        }

        [Fact]
        public void Closed_browser_is_recognised()
        {
            Assert.True(Pipeline.IsBrowserClosed(new Exception("x", new Exception("Target page, context or browser has been closed"))));
            Assert.False(Pipeline.IsBrowserClosed(new TimeoutException("Timeout 30000ms exceeded")));
        }

        [Fact]
        public void Log_writer_reports_whole_lines()
        {
            using var file = new StringWriter();
            using var tee = new TeeTextWriter(null, file);
            List<string> lines = new();
            tee.LineWritten += lines.Add;

            tee.Write("one ");
            tee.WriteLine("line");
            tee.Write("two\r\nthree\n");

            Assert.Equal(new[] { "one line", "two", "three" }, lines);
            Assert.Contains("one line", file.ToString());
        }
    }

    /// <summary>These replace the global internet check, so they must not run alongside other tests that use it.</summary>
    [Collection(NetworkCollection.NAME)]
    public sealed class NetworkTests : IDisposable
    {
        public NetworkTests()
        {
            Network.Check = _ => Task.FromResult(true);
        }

        public void Dispose() => Network.Check = _ => Task.FromResult(true);

        [Fact]
        public async Task Failure_while_offline_does_not_count_against_the_item()
        {
            var item = new FileItem { MediaKey = "k" };

            Network.Check = _ => Task.FromResult(false);
            await Downloader.CountFailure(item);
            Assert.Equal(0, item.FailCount);

            Network.Check = _ => Task.FromResult(true);
            await Downloader.CountFailure(item);
            Assert.Equal(1, item.FailCount);
        }

        [Fact]
        public async Task Waits_until_the_connection_is_back()
        {
            int checks = 0;
            int waitingCalls = 0;
            Network.Check = _ => Task.FromResult(++checks >= 2);

            await Network.WaitUntilOnlineAsync(CancellationToken.None, () => waitingCalls++);

            Assert.Equal(2, checks);
            Assert.Equal(1, waitingCalls);
        }

        [Fact]
        public async Task Remembers_a_drop_during_an_upload()
        {
            DateTime start = DateTime.UtcNow;
            Assert.False(Network.WasOfflineSince(start.AddSeconds(1)));

            Network.Check = _ => Task.FromResult(false);
            await Network.IsOnlineAsync();

            Assert.True(Network.WasOfflineSince(start));
        }

        [Fact]
        public async Task Waiting_stops_when_cancelled()
        {
            Network.Check = _ => Task.FromResult(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Network.WaitUntilOnlineAsync(cts.Token));
        }
    }

    [CollectionDefinition(NAME, DisableParallelization = true)]
    public sealed class NetworkCollection
    {
        public const string NAME = "Network check";
    }
}
