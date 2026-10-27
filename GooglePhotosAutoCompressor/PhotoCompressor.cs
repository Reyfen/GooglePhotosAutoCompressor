using GooglePhotosAutoCompressor.Modules;
using ImageMagick;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Compresses JPEG photos: at most <see cref="Settings.MaxPhotoMegapixels"/> pixels, JPEG quality
    /// <see cref="Settings.PhotoJpegQuality"/>. EXIF (date taken, location, camera, orientation), ICC colour
    /// profile and XMP are kept, so Google Photos places the copy at the original date by itself.
    /// </summary>
    internal static class PhotoCompressor
    {
        private static readonly string[] EXTENSIONS = { ".jpg", ".jpeg" };

        /// <summary>
        /// JPEG only. HEIC is already efficient (re-encoding to JPEG usually grows it), and PNGs are
        /// mostly screenshots or graphics that lossy JPEG would damage.
        /// </summary>
        public static bool IsSupported(string? path) =>
            string.IsNullOrEmpty(path) == false && EXTENSIONS.Contains(Path.GetExtension(path).ToLowerInvariant());

        /// <summary>Same contract as the video path: .part file, checks, then rename.</summary>
        internal static CompressResult CompressOne(FileItem file, string compressDir)
        {
            bool hasSource = File.Exists(file.DownloadPath);

            if (string.IsNullOrEmpty(file.CompressPath) == false && File.Exists(file.CompressPath))
            {
                if (hasSource == false || Check(file.DownloadPath, file.CompressPath) == CompressResult.Ok)
                {
                    // Without the original there is nothing to compare against; the copy was checked when written.
                    file.Verified = hasSource || file.Verified;
                    if (file.Verified)
                    {
                        return CompressResult.Ok;
                    }
                }

                if (hasSource == false)
                {
                    return Fail(file);
                }

                File.Delete(file.CompressPath);
            }

            file.CompressPath = null;

            if (hasSource == false)
            {
                return Fail(file);
            }

            Directory.CreateDirectory(compressDir);
            string path = FilePaths.CompressPath(compressDir, file.MediaKey, file.DownloadPath);
            string partPath = Path.ChangeExtension(path, ".part" + Path.GetExtension(path));

            CompressResult result;
            try
            {
                result = Encode(file.DownloadPath, partPath);
                if (result == CompressResult.Ok)
                {
                    result = Check(file.DownloadPath, partPath);
                }
            }
            catch (MagickException e)
            {
                Console.WriteLine($"PhotoCompressor: {file.DownloadPath}: {e.Message}");
                result = CompressResult.Failed;
            }

            if (result != CompressResult.Ok)
            {
                File.Delete(partPath);

                if (result == CompressResult.NotWorthIt)
                {
                    file.CompressionSkipped = true;
                    return result;
                }

                return Fail(file);
            }

            File.Move(partPath, path, true);
            file.CompressPath = path;
            file.Verified = true;
            return CompressResult.Ok;
        }

        private static CompressResult Fail(FileItem file)
        {
            file.FailCount++;
            return CompressResult.Failed;
        }

        private static CompressResult Encode(string inputPath, string outputPath)
        {
            using MagickImage image = new(inputPath);

            // Without a date taken, Google would place the copy at the upload date. Leave such photos alone.
            if (GetDateTaken(image) == null)
            {
                return CompressResult.NotWorthIt;
            }

            (uint width, uint height) = TargetSize(image.Width, image.Height, Settings.Current.MaxPhotoPixels);
            if (width != image.Width || height != image.Height)
            {
                // Pixels are resized as stored; the EXIF orientation tag still applies unchanged.
                image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
            }

            image.Quality = (uint)Settings.Current.PhotoJpegQuality;
            image.Format = MagickFormat.Jpeg;
            image.Write(outputPath);
            return CompressResult.Ok;
        }

        /// <summary>
        /// The copy must open, keep the proportions, orientation and date taken, and be clearly smaller.
        /// </summary>
        internal static CompressResult Check(string originalPath, string outputPath)
        {
            try
            {
                using MagickImage original = new(originalPath);
                using MagickImage output = new(outputPath);

                double originalRatio = (double)original.Width / original.Height;
                double outputRatio = (double)output.Width / output.Height;
                if (Math.Abs(originalRatio - outputRatio) > originalRatio * 0.01)
                {
                    Console.WriteLine($"PhotoCompressor: proportions changed for {outputPath}");
                    return CompressResult.Failed;
                }

                if (original.Orientation != output.Orientation)
                {
                    Console.WriteLine($"PhotoCompressor: orientation tag lost for {outputPath}");
                    return CompressResult.Failed;
                }

                if (GetDateTaken(original) != GetDateTaken(output))
                {
                    Console.WriteLine($"PhotoCompressor: date taken lost for {outputPath}");
                    return CompressResult.Failed;
                }
            }
            catch (MagickException e)
            {
                Console.WriteLine($"PhotoCompressor: cannot read {outputPath}: {e.Message}");
                return CompressResult.Failed;
            }

            if (new FileInfo(outputPath).Length > new FileInfo(originalPath).Length * Settings.Current.MaxCompressedSizeRatio)
            {
                return CompressResult.NotWorthIt;
            }

            return CompressResult.Ok;
        }

        /// <summary>Scales down to at most <paramref name="maxPixels"/>, keeping the proportions. Never scales up.</summary>
        internal static (uint width, uint height) TargetSize(uint width, uint height, long maxPixels)
        {
            long pixels = (long)width * height;
            if (pixels <= maxPixels)
            {
                return (width, height);
            }

            double scale = Math.Sqrt((double)maxPixels / pixels);
            return ((uint)Math.Max(1, Math.Floor(width * scale)), (uint)Math.Max(1, Math.Floor(height * scale)));
        }

        internal static string? GetDateTaken(MagickImage image)
        {
            IExifProfile? exif = image.GetExifProfile();
            return exif?.GetValue(ExifTag.DateTimeOriginal)?.Value
                   ?? exif?.GetValue(ExifTag.DateTimeDigitized)?.Value;
        }
    }
}
