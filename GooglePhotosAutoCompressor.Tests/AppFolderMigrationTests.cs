namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class AppFolderMigrationTests : IDisposable
    {
        private readonly string _downloads = Directory.CreateTempSubdirectory("gpac-downloads-").FullName;
        private string AppFolder => Path.Combine(_downloads, "GooglePhotosAutoCompressor");
        private string NewState => Path.Combine(AppFolder, "progress.json");
        private string OldState => Path.Combine(_downloads, AppFolderMigration.OLD_STATE_NAME);
        private string OldDownloads => Path.Combine(_downloads, AppFolderMigration.OLD_DOWNLOAD_DIR_NAME);
        private string OldCompressed => Path.Combine(_downloads, AppFolderMigration.OLD_COMPRESS_DIR_NAME);

        public void Dispose() => Directory.Delete(_downloads, true);

        /// <summary>What an older version left in Downloads: one item half way (original binned, copy not uploaded).</summary>
        private void OldVersionFiles()
        {
            string download = Path.Combine(OldDownloads, "1a2b3c4d", "VID_1.mp4");
            string copy = Path.Combine(OldCompressed, "VID_1_compressed_1a2b3c4d.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(download)!);
            Directory.CreateDirectory(OldCompressed);
            File.WriteAllText(download, "original");
            File.WriteAllText(copy, "copy");

            FileItemsData old = new(OldState);
            old.AddOrGet(new FileItem { MediaKey = "k1", DownloadPath = download, CompressPath = copy, Verified = true, OriginalDeleted = true });
            old.AddOrGet(new FileItem { MediaKey = "k2", CompressionSkipped = true });
            old.Save();
        }

        private void Run(bool moveWorkFolders = true) => AppFolderMigration.Run(_downloads, AppFolder, NewState, moveWorkFolders);

        [Fact]
        public void Moves_progress_and_files_into_one_folder_keeping_progress()
        {
            OldVersionFiles();

            Run();

            Assert.Empty(Directory.GetFileSystemEntries(_downloads).Where(p => p != AppFolder)); // Downloads is clean
            FileItem item = new FileItemsData(NewState).Items.Single(i => i.MediaKey == "k1");
            Assert.True(item.OriginalDeleted);
            Assert.Equal(Path.Combine(AppFolder, "Originals", "1a2b3c4d", "VID_1.mp4"), item.DownloadPath);
            Assert.Equal(Path.Combine(AppFolder, "Compressed", "VID_1_compressed_1a2b3c4d.mp4"), item.CompressPath);
            Assert.Equal("copy", File.ReadAllText(item.CompressPath)); // the copy still waiting for upload is safe
            Assert.True(ItemRules.CanUpload(item));
            Assert.Equal(2, new FileItemsData(NewState).Items.Count);
        }

        [Fact]
        public void Finishes_a_move_that_was_cut_off_after_the_state_was_written()
        {
            OldVersionFiles();
            // Simulate a crash right after the new state file was written: write it, then put the old one back.
            string oldJson = File.ReadAllText(OldState);
            Run();
            Directory.Move(Path.Combine(AppFolder, "Originals"), OldDownloads);
            Directory.Move(Path.Combine(AppFolder, "Compressed"), OldCompressed);
            File.WriteAllText(OldState, oldJson);

            Run();

            FileItem item = new FileItemsData(NewState).Items.Single(i => i.MediaKey == "k1");
            Assert.True(File.Exists(item.CompressPath));
            Assert.True(File.Exists(item.DownloadPath));
            Assert.False(File.Exists(OldState));
        }

        [Fact]
        public void Running_again_changes_nothing()
        {
            OldVersionFiles();
            Run();
            string json = File.ReadAllText(NewState);

            Run();

            Assert.Equal(json, File.ReadAllText(NewState));
        }

        [Fact]
        public void Own_work_folder_keeps_its_files_and_only_the_progress_moves()
        {
            OldVersionFiles();

            Run(moveWorkFolders: false);

            Assert.True(Directory.Exists(OldDownloads));
            FileItem item = new FileItemsData(NewState).Items.Single(i => i.MediaKey == "k1");
            Assert.Equal(Path.Combine(OldCompressed, "VID_1_compressed_1a2b3c4d.mp4"), item.CompressPath);
            Assert.True(File.Exists(item.CompressPath));
        }

        [Fact]
        public void Folder_already_in_the_new_place_is_not_merged()
        {
            OldVersionFiles();
            Directory.CreateDirectory(Path.Combine(AppFolder, "Compressed"));

            Run();

            // The old compressed folder stays, and the progress still points at it.
            FileItem item = new FileItemsData(NewState).Items.Single(i => i.MediaKey == "k1");
            Assert.Equal(Path.Combine(OldCompressed, "VID_1_compressed_1a2b3c4d.mp4"), item.CompressPath);
            Assert.True(File.Exists(item.CompressPath));
            Assert.Equal(Path.Combine(AppFolder, "Originals", "1a2b3c4d", "VID_1.mp4"), item.DownloadPath);
        }

        [Fact]
        public void Fresh_install_does_nothing()
        {
            Run();

            Assert.False(Directory.Exists(AppFolder));
        }
    }
}
