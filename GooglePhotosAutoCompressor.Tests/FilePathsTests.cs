namespace GooglePhotosAutoCompressor.Tests
{
    public class FilePathsTests
    {
        [Fact]
        public void ShortId_is_stable_and_differs_per_key()
        {
            Assert.Equal(FilePaths.ShortId("AF1QipA"), FilePaths.ShortId("AF1QipA"));
            Assert.NotEqual(FilePaths.ShortId("AF1QipA"), FilePaths.ShortId("AF1QipB"));
            Assert.Matches("^[0-9a-f]{8}$", FilePaths.ShortId("AF1QipA"));
        }

        [Fact]
        public void Same_file_name_from_two_items_gets_two_download_paths()
        {
            string a = FilePaths.DownloadPath("dl", "key-a", "IMG_0001.MOV");
            string b = FilePaths.DownloadPath("dl", "key-b", "IMG_0001.MOV");

            Assert.NotEqual(a, b);
            Assert.Equal("IMG_0001.MOV", Path.GetFileName(a));
        }

        [Fact]
        public void Download_path_ignores_folders_in_suggested_name()
        {
            string path = FilePaths.DownloadPath("dl", "k", "../../evil.mp4");

            Assert.Equal(Path.Combine("dl", FilePaths.ShortId("k"), "evil.mp4"), path);
        }

        [Theory]
        [InlineData("PXL_20251004_135511890_compressed.mp4", true)]
        [InlineData("IMG_0001_compressed_1a2b3c4d.MOV", true)]
        [InlineData("PXL_20251004_135511890.mp4", false)]
        [InlineData("my_compressed_holiday.mp4", false)]
        public void Recognises_own_compressed_files(string name, bool expected)
        {
            Assert.Equal(expected, FilePaths.IsCompressedName(name));
        }

        [Fact]
        public void Compressed_name_is_recognised_as_own()
        {
            Assert.True(FilePaths.IsCompressedName(FilePaths.CompressPath("c", "k", "IMG_0001.MOV")));
        }

        [Fact]
        public void Same_file_name_from_two_items_gets_two_compressed_names()
        {
            string a = Path.GetFileName(FilePaths.CompressPath("c", "key-a", Path.Combine("x", "IMG_0001.MOV")));
            string b = Path.GetFileName(FilePaths.CompressPath("c", "key-b", Path.Combine("y", "IMG_0001.MOV")));

            Assert.NotEqual(a, b);
            Assert.StartsWith("IMG_0001_compressed_", a);
            Assert.EndsWith(".MOV", a);
        }
    }
}
