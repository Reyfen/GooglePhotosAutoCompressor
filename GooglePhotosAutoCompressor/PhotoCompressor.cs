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

        private static CompressResult Encode(string inputPath, string outputPath, DateTime? takenAt = null, int? offsetMs = null)
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

            if (takenAt is DateTime time && image.GetExifProfile() is IExifProfile exif)
            {
                // The original's time as Google Photos shows it (edited there), so the copy lands at the same moment.
                exif.SetValue(ExifTag.DateTimeOriginal, TakenTime.ToExif(time));
                if (offsetMs is int ms)
                {
                    exif.SetValue(ExifTag.OffsetTimeOriginal, TakenTime.ToExifOffset(ms));
                }

                image.SetProfile(exif);
            }

            image.Quality = (uint)Settings.Current.PhotoJpegQuality;
            image.Format = MagickFormat.Jpeg;
            image.Write(outputPath);
            return CompressResult.Ok;
        }

        /// <summary>
        /// The copy must open, keep the proportions, orientation and date taken, and be clearly smaller.
        /// </summary>
        internal static CompressResult Check(string originalPath, string outputPath, string? expectedDateTaken = null)
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

                if ((expectedDateTaken ?? GetDateTaken(original)) != GetDateTaken(output))
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

        /// <summary>
        /// Makes the copy's EXIF date taken match the original's time as Google Photos showed it. Only needed when
        /// that time was edited in Google Photos (the file still has the camera's time). The copy is made again from
        /// the downloaded original, so it is encoded only once. False when that cannot be done.
        /// </summary>
        internal static bool ApplyTakenTime(FileItem file, DateTime takenAt, int? offsetMs)
        {
            if (IsSupported(file.CompressPath) == false || File.Exists(file.CompressPath) == false)
            {
                return false;
            }

            if (TakenTime.PhotoKeepsTime(takenAt, ReadDateTaken(file.CompressPath)))
            {
                return true;
            }

            if (File.Exists(file.DownloadPath) == false)
            {
                return false;
            }

            string partPath = Path.ChangeExtension(file.CompressPath, ".part" + Path.GetExtension(file.CompressPath));
            try
            {
                if (Encode(file.DownloadPath, partPath, takenAt, offsetMs) != CompressResult.Ok
                    || Check(file.DownloadPath, partPath, TakenTime.ToExif(takenAt)) != CompressResult.Ok)
                {
                    return false;
                }

                File.Move(partPath, file.CompressPath, true);
                file.CompressedSize = new FileInfo(file.CompressPath).Length;
                Console.WriteLine($"PhotoCompressor: {Path.GetFileName(file.CompressPath)} now has date taken {TakenTime.Describe(takenAt, offsetMs)} (edited in Google Photos).");
                return true;
            }
            catch (Exception e) when (e is MagickException or IOException)
            {
                Console.WriteLine($"PhotoCompressor: could not set the date of {file.CompressPath}: {e.Message}");
                return false;
            }
            finally
            {
                File.Delete(partPath);
            }
        }

        /// <summary>EXIF date taken of a file ("yyyy:MM:dd HH:mm:ss"), or null when it has none or cannot be read.</summary>
        internal static string? ReadDateTaken(string path)
        {
            try
            {
                using MagickImage image = new();
                image.Ping(path);
                return GetDateTaken(image);
            }
            catch (Exception e) when (e is MagickException or IOException)
            {
                return null;
            }
        }

        internal static string? GetDateTaken(MagickImage image)
        {
            IExifProfile? exif = image.GetExifProfile();
            return exif?.GetValue(ExifTag.DateTimeOriginal)?.Value
                   ?? exif?.GetValue(ExifTag.DateTimeDigitized)?.Value;
        }
    }
}
