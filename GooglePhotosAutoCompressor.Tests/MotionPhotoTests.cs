using System.Text;
using GooglePhotosAutoCompressor.Modules;
using ImageMagick;

namespace GooglePhotosAutoCompressor.Tests
{
    [CollectionDefinition(NAME, DisableParallelization = true)]
    public sealed class SettingsCollection
    {
        /// <summary>Tests that change <see cref="Settings.Current"/> run alone.</summary>
        public const string NAME = "Changes Settings.Current";
    }

    /// <summary>
    /// The three motion photo settings, on a generated motion photo laid out like a Pixel one: JPEG with the motion
    /// photo XMP, then (optionally) a gain map, then an MP4 whose length the XMP gives.
    /// </summary>
    [Collection(SettingsCollection.NAME)]
    public sealed class MotionPhotoTests : IDisposable
    {
        private const int VIDEO = 20_000;
        private readonly string _dir = Directory.CreateTempSubdirectory("gpac-motion-").FullName;
        private readonly Settings _saved = Settings.Current;
        private string OutDir => Path.Combine(_dir, "out");

        public void Dispose()
        {
            Settings.Current = _saved;
            Directory.Delete(_dir, true);
        }

        private static void Use(MotionPhotoMode mode) => Settings.Current = new Settings { MotionPhotos = mode };

        private static byte[] FakeVideo(int length)
        {
            byte[] video = new byte[length];
            new Random(length).NextBytes(video);
            byte[] head = { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' };
            head.CopyTo(video, 0);
            return video;
        }

        private static string Xmp(int? gainMap, int video) =>
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
            "<rdf:Description rdf:about=\"\" xmlns:GCamera=\"http://ns.google.com/photos/1.0/camera/\"" +
            " xmlns:Container=\"http://ns.google.com/photos/1.0/container/\" xmlns:Item=\"http://ns.google.com/photos/1.0/container/item/\"" +
            " GCamera:MotionPhoto=\"1\" GCamera:MotionPhotoVersion=\"1\"><Container:Directory><rdf:Seq>" +
            "<rdf:li rdf:parseType=\"Resource\"><Container:Item Item:Mime=\"image/jpeg\" Item:Semantic=\"Primary\" Item:Length=\"0\" Item:Padding=\"0\"/></rdf:li>" +
            (gainMap is int g ? $"<rdf:li rdf:parseType=\"Resource\"><Container:Item Item:Semantic=\"GainMap\" Item:Mime=\"image/jpeg\" Item:Length=\"{g}\"/></rdf:li>" : "") +
            $"<rdf:li rdf:parseType=\"Resource\"><Container:Item Item:Mime=\"video/mp4\" Item:Semantic=\"MotionPhoto\" Item:Length=\"{video}\" Item:Padding=\"0\"/></rdf:li>" +
            "</rdf:Seq></Container:Directory></rdf:Description></rdf:RDF></x:xmpmeta>";

        /// <summary>A motion photo; <paramref name="withVideo"/> false is a download Google sent without the video.</summary>
        private string MakeMotionPhoto(string name = "PXL_20250906_165308993.MP.jpg", int? gainMap = null, bool withVideo = true)
        {
            string path = Path.Combine(_dir, name);
            using (MagickImage image = new(MagickColors.SkyBlue, 1600, 1200))
            {
                image.AddNoise(NoiseType.Gaussian, 0.5);
                ExifProfile exif = new();
                exif.SetValue(ExifTag.DateTimeOriginal, "2025:09:06 16:53:08");
                image.SetProfile(exif);
                image.SetProfile(new XmpProfile(Encoding.UTF8.GetBytes(Xmp(gainMap, VIDEO))));
                image.Quality = 98;
                image.Write(path, MagickFormat.Jpeg);
            }

            if (withVideo)
            {
                using FileStream file = new(path, FileMode.Append);
                if (gainMap is int g)
                {
                    byte[] map = new byte[g];
                    map[0] = 0xFF;
                    map[1] = 0xD8;
                    file.Write(map);
                }

                file.Write(FakeVideo(VIDEO));
            }

            return path;
        }

        private static FileItem ItemFor(string path) => new()
        {
            MediaType = MediaType.Image,
            MediaKey = "AF1QipMotion",
            // As the large files list shows it (decimal MB read as MiB).
            Size = (long)(new FileInfo(path).Length / 1_000_000.0 * 1024 * 1024),
            DownloadPath = path,
        };

        [Fact]
        public void Tail_is_read_from_the_xmp_and_checked_against_the_file()
        {
            Assert.Equal(VIDEO, MotionPhoto.TailLength(MakeMotionPhoto()));
            Assert.Equal(VIDEO + 5_000, MotionPhoto.TailLength(MakeMotionPhoto("hdr.MP.jpg", gainMap: 5_000)));
            Assert.Null(MotionPhoto.TailLength(MakeMotionPhoto("short.MP.jpg", withVideo: false)));
        }

        [Fact]
        public void Older_pixels_give_the_video_offset_from_the_end()
        {
            Assert.Equal((123L, 123L), MotionPhoto.ParseTail("GCamera:MicroVideo=\"1\" GCamera:MicroVideoOffset=\"123\""));
            Assert.Null(MotionPhoto.ParseTail("no motion here"));
        }

        [Fact]
        public void Keep_motion_puts_the_original_video_back_after_the_smaller_picture()
        {
            Use(MotionPhotoMode.KeepMotion);
            string original = MakeMotionPhoto(gainMap: 5_000);
            FileItem item = ItemFor(original);

            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(MotionPhoto.CopyKeepsMotion(original, item.CompressPath!));
            Assert.True(MotionPhoto.ClaimsMotion(item.CompressPath!));
            Assert.True(new FileInfo(item.CompressPath!).Length < new FileInfo(original).Length);
            Assert.True(PhotoCompressor.CanRead(item.CompressPath!));
            Assert.True(MotionPhoto.CopyIsFine(item));
        }

        [Fact]
        public void Keep_motion_survives_setting_the_date_taken_again()
        {
            Use(MotionPhotoMode.KeepMotion);
            string original = MakeMotionPhoto();
            FileItem item = ItemFor(original);
            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(PhotoCompressor.ApplyTakenTime(item, new DateTime(2025, 9, 6, 18, 0, 0), 3 * 3600_000));

            Assert.True(MotionPhoto.CopyKeepsMotion(original, item.CompressPath!));
            Assert.Equal("2025:09:06 18:00:00", PhotoCompressor.ReadDateTaken(item.CompressPath!));
        }

        [Fact]
        public void Keep_motion_leaves_alone_a_download_without_its_video()
        {
            Use(MotionPhotoMode.KeepMotion);
            FileItem item = ItemFor(MakeMotionPhoto(withVideo: false));

            Assert.Equal(CompressResult.NotWorthIt, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(item.CompressionSkipped);
            Assert.Equal(0, item.FailCount);
            Assert.False(ItemRules.IsReadyForBin(item));
        }

        [Fact]
        public void Still_makes_a_still_photo_that_claims_no_video()
        {
            Use(MotionPhotoMode.Still);
            string original = MakeMotionPhoto();
            FileItem item = ItemFor(original);

            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            Assert.Null(MotionPhoto.TailLength(item.CompressPath));
            Assert.False(MotionPhoto.ClaimsMotion(item.CompressPath!));
            Assert.True(MotionPhoto.CopyIsFine(item));
        }

        [Fact]
        public void Leave_alone_makes_no_copy()
        {
            Use(MotionPhotoMode.LeaveAlone);
            FileItem item = ItemFor(MakeMotionPhoto());

            Assert.Equal(CompressResult.NotWorthIt, PhotoCompressor.CompressOne(item, OutDir));

            Assert.True(item.CompressionSkipped);
            Assert.Null(item.CompressPath);
        }

        [Fact]
        public void A_still_copy_made_earlier_may_not_send_the_original_to_the_bin_with_keep_motion()
        {
            Use(MotionPhotoMode.Still);
            string original = MakeMotionPhoto();
            FileItem item = ItemFor(original);
            Assert.Equal(CompressResult.Ok, PhotoCompressor.CompressOne(item, OutDir));

            Use(MotionPhotoMode.KeepMotion);
            Assert.False(MotionPhoto.CopyIsFine(item));
            Use(MotionPhotoMode.LeaveAlone);
            Assert.False(MotionPhoto.CopyIsFine(item));
        }

        [Fact]
        public void Normal_photos_are_not_affected()
        {
            Use(MotionPhotoMode.LeaveAlone);
            string path = Path.Combine(_dir, "normal.jpg");
            using (MagickImage image = new(MagickColors.SkyBlue, 10, 10))
            {
                image.Write(path, MagickFormat.Jpeg);
            }

            Assert.False(MotionPhoto.Is(path));
            Assert.True(MotionPhoto.CopyIsFine(new FileItem { MediaKey = "n", DownloadPath = path }));
        }
    }
}
