using System.Text;
using System.Text.RegularExpressions;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Motion photos (Pixel ".MP.jpg", Samsung) keep a short video inside the JPEG, after the picture; newer Pixels
    /// also put an HDR gain map (a second JPEG) between them. The XMP "Container:Directory" lists those parts with
    /// their lengths, counted from the end of the file. Re-encoding the picture drops everything after it, so the
    /// copy was a still photo (found 2026-11-10: 187 copies had lost their video). <see cref="Settings.MotionPhotos"/>
    /// decides: keep the motion (default since 2026-11-11, checked live: copies play as motion photos; the original
    /// parts after the picture are put back unchanged, so their lengths in the XMP still fit), leave them alone, or
    /// make a still photo.
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

            return PhotoCompressor.IsSupported(path) && HasMotionMarker(ReadHeader(path));
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

        /// <summary>The file's XMP says it is a motion photo (a copy keeps the original's XMP).</summary>
        public static bool ClaimsMotion(string path) => HasMotionMarker(ReadHeader(path));

        /// <summary>The first bytes of the file as text (XMP is ASCII), or "" when it can't be read.</summary>
        private static string ReadHeader(string path)
        {
            try
            {
                if (File.Exists(path) == false)
                {
                    return "";
                }

                using FileStream stream = File.OpenRead(path);
                byte[] buffer = new byte[Math.Min(HEADER_BYTES, stream.Length)];
                int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                return Encoding.Latin1.GetString(buffer, 0, read);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return "";
            }
        }

        /// <summary>
        /// The length of everything after the picture (gain map, video), from the XMP; null when there is no video
        /// in the file. A download Google sent without the video (seen: smaller than listed) gives null.
        /// </summary>
        public static long? TailLength(string? path)
        {
            if (string.IsNullOrEmpty(path) || File.Exists(path) == false)
            {
                return null;
            }

            (long tail, long video)? parts = ParseTail(ReadHeader(path));
            if (parts == null)
            {
                return null;
            }

            long fileLength = new FileInfo(path).Length;
            if (parts.Value.tail >= fileLength || parts.Value.video <= 8)
            {
                return null;
            }

            // The video (MP4) must really be where the XMP says: its first box is "ftyp".
            try
            {
                using FileStream stream = File.OpenRead(path);
                stream.Seek(fileLength - parts.Value.video + 4, SeekOrigin.Begin);
                byte[] box = new byte[4];
                stream.ReadExactly(box);
                return Encoding.ASCII.GetString(box) == "ftyp" ? parts.Value.tail : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// From the XMP text: the length of all parts after the picture, and of the video (the last part).
        /// Container:Directory (motion photo format v1) or, on older Pixels, GCamera:MicroVideoOffset.
        /// </summary>
        internal static (long tail, long video)? ParseTail(string xmp)
        {
            long tail = 0;
            long? video = null;
            foreach (Match item in Regex.Matches(xmp, @"<Container:Item\b([^>]*)>", RegexOptions.Singleline))
            {
                string attributes = item.Groups[1].Value;
                string semantic = Attribute(attributes, "Item:Semantic") ?? "";
                if (semantic == "Primary")
                {
                    continue;
                }

                long length = long.TryParse(Attribute(attributes, "Item:Length"), out long l) ? l : 0;
                long padding = long.TryParse(Attribute(attributes, "Item:Padding"), out long p) ? p : 0;
                if (length <= 0)
                {
                    return null; // a part of unknown size: can't tell where the picture ends
                }

                tail += length + padding;
                if (semantic == "MotionPhoto")
                {
                    video = length;
                }
            }

            if (video != null)
            {
                return (tail, video.Value);
            }

            Match micro = Regex.Match(xmp, @"MicroVideoOffset=""(\d+)""");
            if (micro.Success && long.TryParse(micro.Groups[1].Value, out long offset) && offset > 0)
            {
                return (offset, offset);
            }

            return null;
        }

        private static string? Attribute(string attributes, string name)
        {
            Match m = Regex.Match(attributes, Regex.Escape(name) + @"=""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Puts the original's parts after the picture (gain map, video) unchanged at the end of the copy. The copy
        /// must still carry the motion XMP (Magick keeps it). False when that can't be done.
        /// </summary>
        public static bool AppendTail(string originalPath, string copyPath)
        {
            long? tail = TailLength(originalPath);
            if (tail == null || HasMotionMarker(ReadHeader(copyPath)) == false)
            {
                return false;
            }

            using FileStream original = File.OpenRead(originalPath);
            original.Seek(-tail.Value, SeekOrigin.End);
            using FileStream copy = new(copyPath, FileMode.Append, FileAccess.Write);
            original.CopyTo(copy);
            return true;
        }

        /// <summary>The copy ends with exactly the original's video (and gain map), where its XMP says.</summary>
        public static bool CopyKeepsMotion(string originalPath, string copyPath)
        {
            long? tail = TailLength(originalPath);
            if (tail == null || TailLength(copyPath) != tail)
            {
                return false;
            }

            using FileStream original = File.OpenRead(originalPath);
            using FileStream copy = File.OpenRead(copyPath);
            original.Seek(-tail.Value, SeekOrigin.End);
            copy.Seek(-tail.Value, SeekOrigin.End);
            byte[] a = new byte[81920];
            byte[] b = new byte[81920];
            int read;
            while ((read = original.Read(a, 0, a.Length)) > 0)
            {
                copy.ReadExactly(b, 0, read);
                if (a.AsSpan(0, read).SequenceEqual(b.AsSpan(0, read)) == false)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Why this motion photo can't be compressed with the current setting, or null when it can. Only for an
        /// original still in the library; the download must hold the video for "keep motion" and for "still" (a
        /// download without it may be cut off, and Google keeps more than it sent).
        /// </summary>
        public static string? WhyLeftAlone(string? downloadPath)
        {
            if (Settings.Current.MotionPhotos == MotionPhotoMode.LeaveAlone)
            {
                return "motion photos are left alone (Settings › What to compress)";
            }

            return TailLength(downloadPath) == null ? "its download has no video" : null;
        }

        /// <summary>
        /// May this item's original go to the bin? For a motion photo: never when they are left alone; with "keep
        /// motion" only when the copy ends with the original's video. Not a motion photo: true.
        /// </summary>
        public static bool CopyIsFine(FileItem item)
        {
            if (Is(item.DownloadPath) == false && Is(item.CompressPath) == false)
            {
                return true;
            }

            return Settings.Current.MotionPhotos switch
            {
                MotionPhotoMode.KeepMotion => File.Exists(item.CompressPath) && (File.Exists(item.DownloadPath)
                    ? CopyKeepsMotion(item.DownloadPath!, item.CompressPath!)
                    : TailLength(item.CompressPath) != null),
                MotionPhotoMode.Still => true,
                _ => false,
            };
        }

        /// <summary>
        /// Leaves a motion photo alone: deletes its download and any copy made of it, never moves its original to the
        /// bin. Only for an original that is still in the library.
        /// </summary>
        public static void LeaveAlone(FileItem item, string reason)
        {
            Console.WriteLine($"Motion photo left alone ({reason}): {item.DownloadPath ?? item.CompressPath}");
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
