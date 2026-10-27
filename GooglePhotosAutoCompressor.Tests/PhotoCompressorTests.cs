using GooglePhotosAutoCompressor.Modules;
using ImageMagick;

namespace GooglePhotosAutoCompressor.Tests
{
    /// <summary>Photo compression on generated JPEGs. No ffmpeg or browser needed.</summary>
    public sealed class PhotoCompressorTests : IDisposable
    {
        private const string DATE_TAKEN = "2021:06:01 10:20:30";
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-photo-").FullName;
        private string OutDir => Path.Combine(_dir, "out");

        public void Dispose() => Directory.Delete(_dir, true);

        /// <summary>A noisy photo (noise compresses badly, like real detail) saved at high quality.</summary>
        private string MakeJpeg(string name, uint width = 1600, uint height = 1200, int quality = 98,
            string? dateTaken = DATE_TAKEN, Orientation orientation = Orientation.TopLeft)
        {
            string path = Path.Combine(_dir, name);
            using MagickImage image = new(MagickColors.SkyBlue, width, height);
            image.AddNoise(NoiseType.Gaussian, 0.5);

            ExifProfile exif = new();
            if (dateTaken != null)
            {
                exif.SetValue(ExifTag.DateTimeOriginal, dateTaken);
            }
            exif.SetValue(ExifTag.Make, "TestCam");
            // Cameras store rotation as the EXIF orientation tag.
            exif.SetValue(ExifTag.Orientation, (ushort)orientation);
            image.SetProfile(exif);
            image.Orientation = orientation; // ImageMagick writes the tag from this property

            image.Quality = (uint)quality;
            image.Write(path, MagickFormat.Jpeg);
            return path;
        }

        private static FileItem ItemFor(string path) => new()
        {
            MediaType = MediaType.Image,
            MediaKey = Path.GetFileName(path),
            Size = new FileInfo(path).Length,
            DownloadPath = path,
        };

        [Fact]
        public void Compresses_and_verifies_photo()
        {
            FileItem item = ItemFor(MakeJpeg("photo.jpg"));

            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(item.Verified);
            Assert.False(item.CompressionSkipped);
            Assert.True(new FileInfo(item.CompressPath).Length <= item.Size * Const.MAX_COMPRESSED_SIZE_RATIO);
            Assert.True(FilePaths.IsCompressedName(Path.GetFileName(item.CompressPath)));
            Assert.Empty(Directory.GetFiles(OutDir, "*.part*"));
        }

        [Fact]
        public void Keeps_date_taken_orientation_and_other_exif()
        {
            FileItem item = ItemFor(MakeJpeg("rotated.jpg", orientation: Orientation.RightTop));

            PhotoCompressor.CompressOne(item, OutDir);

            using MagickImage original = new(item.DownloadPath);
            Assert.Equal(Orientation.RightTop, original.Orientation); // test setup is right

            using MagickImage copy = new(item.CompressPath);
            Assert.Equal(DATE_TAKEN, PhotoCompressor.GetDateTaken(copy));
            Assert.Equal(Orientation.RightTop, copy.Orientation);
            Assert.Equal("TestCam", copy.GetExifProfile()?.GetValue(ExifTag.Make)?.Value);
        }

        [Fact]
        public void Keeps_size_of_photo_under_the_pixel_limit()
        {
            FileItem item = ItemFor(MakeJpeg("small.jpg", 1600, 1200));

            PhotoCompressor.CompressOne(item, OutDir);

            using MagickImage copy = new(item.CompressPath);
            Assert.Equal((1600u, 1200u), (copy.Width, copy.Height));
        }

        [Fact]
        public void Shrinks_photo_over_the_pixel_limit_keeping_proportions()
        {
            // 20 MP, above the 16 MP limit.
            FileItem item = ItemFor(MakeJpeg("big.jpg", 5000, 4000, quality: 95));

            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            using MagickImage copy = new(item.CompressPath);
            Assert.True((long)copy.Width * copy.Height <= Const.MAX_PHOTO_PIXELS);
            Assert.Equal(5000.0 / 4000, (double)copy.Width / copy.Height, 2);
        }

        [Fact]
        public void Skips_photo_without_date_taken()
        {
            FileItem item = ItemFor(MakeJpeg("nodate.jpg", dateTaken: null));

            Assert.Equal(CompressResult.NotWorthIt, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(item.CompressionSkipped);
            Assert.False(item.Verified);
            Assert.Null(item.CompressPath);
            Assert.Empty(Directory.GetFiles(OutDir));
        }

        [Fact]
        public void Skips_photo_that_is_already_compressed()
        {
            // Re-saving a low quality JPEG at quality 85 makes it bigger, not smaller.
            FileItem item = ItemFor(MakeJpeg("lowq.jpg", quality: 40));

            Assert.Equal(CompressResult.NotWorthIt, PhotoCompressor.CompressOne(item, OutDir));
            Assert.True(item.CompressionSkipped);
            Assert.Empty(Directory.GetFiles(OutDir));
        }

        [Fact]
        public void Reuses_good_copy_from_earlier_run()
        {
            FileItem item = ItemFor(MakeJpeg("photo.jpg"));
            PhotoCompressor.CompressOne(item, OutDir);
            DateTime written = File.GetLastWriteTimeUtc(item.CompressPath);
            item.Verified = false;

            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(item.Verified);
            Assert.Equal(written, File.GetLastWriteTimeUtc(item.CompressPath));
        }

        [Fact]
        public void Replaces_broken_copy_from_earlier_run()
        {
            FileItem item = ItemFor(MakeJpeg("photo.jpg"));
            Directory.CreateDirectory(OutDir);
            string broken = FilePaths.CompressPath(OutDir, item.MediaKey, item.DownloadPath);
            File.WriteAllText(broken, "not a jpeg");
            item.CompressPath = broken;

            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            using MagickImage copy = new(item.CompressPath);
            Assert.Equal(DATE_TAKEN, PhotoCompressor.GetDateTaken(copy));
        }

        [Fact]
        public void Missing_download_counts_as_failure()
        {
            var item = new FileItem { MediaType = MediaType.Image, MediaKey = "k", DownloadPath = Path.Combine(_dir, "gone.jpg") };

            Assert.Equal(CompressResult.Failed, PhotoCompressor.CompressOne(item, OutDir));
            Assert.Equal(1, item.FailCount);
        }

        [Fact]
        public void Unreadable_download_counts_as_failure()
        {
            string path = Path.Combine(_dir, "broken.jpg");
            File.WriteAllText(path, "not a jpeg");

            FileItem item = ItemFor(path);
            Assert.Equal(CompressResult.Failed, PhotoCompressor.CompressOne(item, OutDir));
            Assert.Equal(1, item.FailCount);
            Assert.False(Directory.Exists(OutDir) && Directory.GetFiles(OutDir).Length > 0);
        }

        [Fact]
        public void Compressor_sends_jpeg_to_photo_path_by_file_type()
        {
            // Tile looked like a video (duration not loaded yet); the downloaded file is a JPEG.
            string path = MakeJpeg("photo.jpg");
            var item = new FileItem { MediaType = MediaType.Video, MediaKey = "k", Size = new FileInfo(path).Length, DownloadPath = path };

            // No ffmpeg needed for photos.
            Assert.Equal(CompressResult.Ok, Compressor.CompressOne(null!, item, OutDir));
            Assert.True(item.Verified);
        }

        [Theory]
        [InlineData("IMG_1.HEIC")]
        [InlineData("screen.png")]
        public void Compressor_skips_other_photo_formats(string name)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, new byte[100]);
            var item = new FileItem { MediaType = MediaType.Image, MediaKey = "k", Size = 100, DownloadPath = path };

            Assert.Equal(CompressResult.NotWorthIt, Compressor.CompressOne(null!, item, OutDir));
            Assert.True(item.CompressionSkipped);
        }

        [Theory]
        [InlineData("a.jpg", true)]
        [InlineData("A.JPEG", true)]
        [InlineData("a.heic", false)]
        [InlineData("a.png", false)]
        [InlineData("a.mp4", false)]
        [InlineData(null, false)]
        public void Supports_only_jpeg(string? path, bool expected)
        {
            Assert.Equal(expected, PhotoCompressor.IsSupported(path));
        }

        [Theory]
        [InlineData(4000u, 3000u, 4000u, 3000u)]   // 12 MP: unchanged
        [InlineData(4000u, 4000u, 4000u, 4000u)]   // exactly 16 MP: unchanged
        [InlineData(8000u, 6000u, 4618u, 3464u)]   // 48 MP -> about 16 MP
        [InlineData(6000u, 8000u, 3464u, 4618u)]   // portrait
        [InlineData(20000u, 1000u, 17888u, 894u)]  // panorama
        public void Target_size_stays_under_the_pixel_limit(uint w, uint h, uint expectedW, uint expectedH)
        {
            (uint tw, uint th) = PhotoCompressor.TargetSize(w, h, 16_000_000);

            Assert.Equal((expectedW, expectedH), (tw, th));
            Assert.True((long)tw * th <= 16_000_000);
        }
    }
}
