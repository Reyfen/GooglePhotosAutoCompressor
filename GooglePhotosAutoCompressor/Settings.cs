using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GooglePhotosAutoCompressor
{
    internal enum VideoResolution
    {
        /// <summary>Half width and height of the original.</summary>
        Half,
        /// <summary>Shorter side at most 1080 px.</summary>
        Max1080p,
        /// <summary>Shorter side at most 720 px.</summary>
        Max720p,
    }

    /// <summary>
    /// What to compress, how, and how much per run. Edited in the app and saved as JSON next to the
    /// browser profile. Defaults are the values the app always used.
    /// </summary>
    internal sealed class Settings
    {
        /// <summary>The settings in use. Replaced as a whole (never edited in place) when a run starts.</summary>
        public static Settings Current { get; set; } = new();

        /// <summary>Settings, logs, browser profile and downloaded ffmpeg: %LOCALAPPDATA% on Windows, ~/.local/share on Linux.</summary>
        public static readonly string CONFIG_DIR = Path.Combine(LocalAppData(), "GooglePhotosAutoCompressor");

        /// <summary>
        /// On Linux .NET returns "" for LocalApplicationData when ~/.local/share does not exist yet (fresh
        /// accounts), which would put all app files in the current folder. Create it, and fall back to $HOME.
        /// </summary>
        internal static string LocalAppData()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            if (string.IsNullOrEmpty(dir) == false)
            {
                return dir;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, ".local", "share");
        }

        public static readonly string DEFAULT_PATH = Path.Combine(CONFIG_DIR, "settings.json");

        // What to compress
        public bool CompressVideos { get; set; } = true;
        public bool CompressPhotos { get; set; } = true;
        /// <summary>Videos below this bitrate are already small for their length.</summary>
        public double MinVideoMbPerSecond { get; set; } = 0.8;
        public double MinPhotoMb { get; set; } = 2;

        // Quality
        [JsonConverter(typeof(StringEnumConverter))]
        public VideoResolution VideoResolution { get; set; } = VideoResolution.Half;
        /// <summary>x264 CRF: lower is better quality and bigger. 18–28 is the useful range.</summary>
        public int VideoCrf { get; set; } = 20;
        /// <summary>16 MP is the size Google's own "Storage saver" quality keeps.</summary>
        public double MaxPhotoMegapixels { get; set; } = 16;
        public int PhotoJpegQuality { get; set; } = 85;
        /// <summary>A copy must be at least this much smaller, or the original is kept.</summary>
        public int MinSavingPercent { get; set; } = 10;

        // Limits
        /// <summary>Items in progress at once (downloaded but not finished).</summary>
        public int ItemsPerBatch { get; set; } = 10;
        /// <summary>Start the next batch automatically until nothing is left.</summary>
        public bool RepeatUntilDone { get; set; }
        public int ParallelDownloads { get; set; } = 5;
        /// <summary>Downloads stop when the disk would have less than this left.</summary>
        public double MinFreeDiskGb { get; set; } = 2;
        /// <summary>After this many failed attempts an item is left alone.</summary>
        public int MaxFails { get; set; } = 3;

        // Safety and disk
        /// <summary>Show the list and ask before moving originals to the bin.</summary>
        public bool ConfirmBeforeBin { get; set; } = true;
        /// <summary>Delete the downloaded original and the copy from this PC once an item is finished.</summary>
        public bool DeleteLocalFilesWhenDone { get; set; }
        /// <summary>Where downloads and compressed copies are kept. Empty means the Downloads folder.</summary>
        public string WorkFolder { get; set; } = "";

        [JsonIgnore]
        public long MinPhotoBytes => (long)(MinPhotoMb * 1024 * 1024);
        [JsonIgnore]
        public double MinVideoBytesPerSecond => MinVideoMbPerSecond * 1024 * 1024;
        [JsonIgnore]
        public long MaxPhotoPixels => (long)(MaxPhotoMegapixels * 1_000_000);
        /// <summary>A copy larger than this share of the original is not worth replacing it.</summary>
        [JsonIgnore]
        public double MaxCompressedSizeRatio => 1 - MinSavingPercent / 100.0;
        [JsonIgnore]
        public long MinFreeDiskBytes => (long)(MinFreeDiskGb * 1024 * 1024 * 1024);
        [JsonIgnore]
        public string WorkFolderOrDefault => string.IsNullOrWhiteSpace(WorkFolder) ? Const.DOWNLOADS_PATH : WorkFolder;

        /// <summary>Brings every value into a range the app can work with.</summary>
        public Settings Normalized()
        {
            Settings s = Clone();
            s.MinVideoMbPerSecond = Math.Clamp(s.MinVideoMbPerSecond, 0, 100);
            s.MinPhotoMb = Math.Clamp(s.MinPhotoMb, 0, 1000);
            s.VideoCrf = Math.Clamp(s.VideoCrf, 0, 51);
            s.MaxPhotoMegapixels = Math.Clamp(s.MaxPhotoMegapixels, 1, 200);
            s.PhotoJpegQuality = Math.Clamp(s.PhotoJpegQuality, 30, 100);
            s.MinSavingPercent = Math.Clamp(s.MinSavingPercent, 1, 90);
            s.ItemsPerBatch = Math.Clamp(s.ItemsPerBatch, 1, 1000);
            s.ParallelDownloads = Math.Clamp(s.ParallelDownloads, 1, 10);
            s.MinFreeDiskGb = Math.Clamp(s.MinFreeDiskGb, 0, 10_000);
            s.MaxFails = Math.Clamp(s.MaxFails, 1, 100);
            s.WorkFolder = s.WorkFolder?.Trim() ?? "";
            return s;
        }

        public Settings Clone() => (Settings)MemberwiseClone();

        /// <summary>Loads saved settings, or defaults when there are none or the file is unreadable.</summary>
        public static Settings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return (JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path)) ?? new Settings()).Normalized();
                }
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Settings: {path} is unreadable ({e.Message}), using defaults.");
            }

            return new Settings();
        }

        public void Save(string path)
        {
            DurableFile.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
