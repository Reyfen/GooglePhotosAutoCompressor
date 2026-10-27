namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class FileItemsDataTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-state-").FullName;
        private string StatePath => Path.Combine(_dir, "state.json");

        public void Dispose() => Directory.Delete(_dir, true);

        private static FileItem Item(string key) => new() { MediaKey = key, Size = 1, Duration = TimeSpan.FromSeconds(1) };

        [Fact]
        public void Save_and_load_round_trip()
        {
            var data = new FileItemsData(StatePath);
            data.AddOrGet(Item("a")).OriginalDeleted = true;
            data.Save();

            var loaded = new FileItemsData(StatePath);
            Assert.True(Assert.Single(loaded.Items).OriginalDeleted);
            Assert.False(File.Exists(StatePath + ".tmp"));
        }

        [Fact]
        public void AddOrGet_returns_stored_item_for_same_key()
        {
            var data = new FileItemsData(StatePath);
            FileItem first = data.AddOrGet(Item("a"));

            Assert.Same(first, data.AddOrGet(Item("a")));
            Assert.Single(data.Items);
        }

        [Fact]
        public void Corrupt_state_falls_back_to_backup()
        {
            var data = new FileItemsData(StatePath);
            data.AddOrGet(Item("a"));
            data.Save();
            data.AddOrGet(Item("b"));
            data.Save(); // backup now holds only "a"

            File.WriteAllText(StatePath, "{ half written");

            var loaded = new FileItemsData(StatePath);
            Assert.Equal("a", Assert.Single(loaded.Items).MediaKey);
        }

        [Fact]
        public void Corrupt_state_without_backup_throws_instead_of_starting_empty()
        {
            File.WriteAllText(StatePath, "{ half written");

            Assert.Throws<InvalidDataException>(() => new FileItemsData(StatePath));
        }

        [Fact]
        public async Task Parallel_saves_and_adds_do_not_throw()
        {
            var data = new FileItemsData(StatePath);

            Task saves = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    data.Save();
                }
            });

            for (int i = 0; i < 2000; i++)
            {
                data.AddOrGet(Item("k" + i));
            }

            await saves;
            data.Save();

            Assert.Equal(2000, new FileItemsData(StatePath).Items.Count);
        }

        [Fact]
        public void Reads_state_written_by_old_version()
        {
            File.WriteAllText(StatePath, """
                [{ "MediaKey": "AF1Q", "Date": "2025-10-04", "Duration": "00:00:05", "Size": 13212057,
                   "DownloadPath": "C:\\x.mp4", "CompressPath": "C:\\x_compressed.mp4",
                   "OriginalDeleted": false, "CompressUploaded": true, "DateSet": true }]
                """);

            FileItem item = Assert.Single(new FileItemsData(StatePath).Items);
            Assert.Equal(new DateOnly(2025, 10, 4), item.Date);
            Assert.True(item.CompressUploaded);
        }
    }
}
