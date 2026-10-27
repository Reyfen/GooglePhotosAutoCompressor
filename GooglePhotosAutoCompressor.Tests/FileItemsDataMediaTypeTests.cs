namespace GooglePhotosAutoCompressor.Tests
{
    public sealed class FileItemsDataMediaTypeTests : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-mediatype-").FullName;
        private string StatePath => Path.Combine(_dir, "state.json");

        public void Dispose() => Directory.Delete(_dir, true);

        [Fact]
        public void Old_state_file_without_media_type_loads_as_video()
        {
            // Written by versions before photo support.
            File.WriteAllText(StatePath, """[{"MediaKey":"k","Date":"2024-10-04","Duration":"00:00:09","Size":8388608}]""");

            FileItem item = Assert.Single(new FileItemsData(StatePath).Items);

            Assert.Equal(MediaType.Video, item.MediaType);
        }

        [Fact]
        public void Media_type_survives_save_and_load()
        {
            var data = new FileItemsData(StatePath);
            data.AddOrGet(new FileItem { MediaType = MediaType.Image, MediaKey = "p", Size = 1 });
            data.Save();

            Assert.Equal(MediaType.Image, Assert.Single(new FileItemsData(StatePath).Items).MediaType);
        }
    }
}
