using System.Text;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Motion photos (Pixel ".MP.jpg", Samsung) keep a short video inside the JPEG, after the picture. Re-encoding the
    /// picture drops that video, so the copy would be a still photo (found 2026-11-10: 187 copies had lost it). They
    /// are left alone: no copy, and the original stays in the library. Their download can also be smaller than the
    /// listed size (Google sometimes leaves the video out), which once made them fail the download check three times.
    /// </summary>
    internal static class MotionPhoto
    {
        /// <summary>The XMP of a motion photo sits near the start of the file.</summary>
        private const int HEADER_BYTES = 256 * 1024;

        private static readonly string[] MARKERS = { "MotionPhoto", "MicroVideo" };

        /// <summary>True for a motion photo, by its name (".MP.jpg", ".MP~2.jpg") or its XMP.</summary>
        public static bool Is(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (HasMotionName(path))
            {
                return true;
            }

            if (PhotoCompressor.IsSupported(path) == false || File.Exists(path) == false)
            {
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(path);
                byte[] buffer = new byte[Math.Min(HEADER_BYTES, stream.Length)];
                int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                return HasMotionMarker(Encoding.ASCII.GetString(buffer, 0, read));
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>Pixel names: PXL_20250906_165308993.MP.jpg, also with ~2 for duplicates.</summary>
        public static bool HasMotionName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int tilde = name.IndexOf('~');
            if (tilde >= 0)
            {
                name = name[..tilde];
            }

            return name.EndsWith(".MP", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasMotionMarker(string header) =>
            MARKERS.Any(m => header.Contains(m, StringComparison.Ordinal));

        /// <summary>
        /// Leaves a motion photo alone: deletes its download and any copy made of it, never moves its original to the
        /// bin. Only for an original that is still in the library.
        /// </summary>
        public static void LeaveAlone(FileItem item)
        {
            Console.WriteLine($"Motion photo left alone (compressing it would drop its video): {item.DownloadPath ?? item.CompressPath}");
            foreach (string? path in new[] { item.CompressPath, item.DownloadPath })
            {
                try
                {
                    if (string.IsNullOrEmpty(path) == false && File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"Could not delete {path}: {e.Message}");
                }
            }

            item.CompressPath = null;
            item.DownloadPath = null;
            item.Verified = false;
            item.CompressionSkipped = true;
        }
    }
}
