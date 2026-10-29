namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class CleanWorkFolderTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-clean-").FullName;
        private string Originals => Path.Combine(_dir, "Originals");
        private string Compressed => Path.Combine(_dir, "Compressed");

        public void Dispose() => Directory.Delete(_dir, true);

        private string File(string folder, string name, int bytes = 10)
        {
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        [Fact]
        public void Deletes_everything_except_copies_still_waiting_for_upload()
        {
            var state = new FileItemsData(Path.Combine(_dir, "progress.json"));
            // Done: both files can go.
            var done = state.AddOrGet(new FileItem { MediaKey = "done", DownloadPath = File(Originals, "a1/A.mp4"), CompressPath = File(Compressed, "A_compressed_a1.mp4"),
                Verified = true, OriginalDeleted = true, CompressUploaded = true, DateSet = true, CompressedSize = 10 });
            // Original in the Google bin, copy not uploaded yet: the copy must stay.
            var waiting = state.AddOrGet(new FileItem { MediaKey = "wait", DownloadPath = File(Originals, "b2/B.mp4"), CompressPath = File(Compressed, "B_compressed_b2.mp4", 20),
                Verified = true, OriginalDeleted = true });
            // Compressed, original still in the library: copy can go, item is compressed again next run.
            var compressed = state.AddOrGet(new FileItem { MediaKey = "comp", DownloadPath = File(Originals, "c3/C.mp4"), CompressPath = File(Compressed, "C_compressed_c3.mp4"),
                Verified = true, CompressedSize = 10 });
            string partial = File(Originals, "d4/D.mp4.part");
            state.Save();

            LocalFiles.CleanPlan plan = LocalFiles.PlanClean(state, Originals, Compressed);
            Assert.Equal(new[] { waiting.CompressPath }, plan.Keep);
            Assert.Equal(20, plan.KeepBytes);
            Assert.Equal(6, plan.Delete.Count);

            (int files, long bytes) = LocalFiles.Clean(state, plan, Originals, Compressed);

            Assert.Equal(6, files);
            Assert.Equal(60, bytes);
            Assert.True(System.IO.File.Exists(waiting.CompressPath));
            Assert.False(System.IO.File.Exists(partial));
            Assert.Equal(new[] { Path.GetDirectoryName(waiting.CompressPath) }, Directory.GetDirectories(_dir).Where(d => Directory.EnumerateFileSystemEntries(d).Any()));
            Assert.Empty(Directory.GetDirectories(Originals)); // per-item folders removed

            // Progress: the waiting item can still be uploaded; the compressed one starts again; the done one stays done.
            var reloaded = new FileItemsData(Path.Combine(_dir, "progress.json")).Items.ToDictionary(i => i.MediaKey);
            Assert.True(ItemRules.CanUpload(reloaded["wait"]));
            Assert.False(reloaded["comp"].Verified);
            Assert.True(ItemRules.NeedsDownload(reloaded["comp"]));
            Assert.True(ItemRules.IsDone(reloaded["done"]));
            Assert.Equal(10, reloaded["done"].CompressedSize); // "space saved" still counts it
        }

        [Fact]
        public void Works_without_a_progress_file_or_folders()
        {
            LocalFiles.CleanPlan plan = LocalFiles.PlanClean(null, Originals, Compressed);
            Assert.Empty(plan.Delete);
            Assert.Equal((0, 0L), LocalFiles.Clean(null, plan, Originals, Compressed));
        }
    }
}
