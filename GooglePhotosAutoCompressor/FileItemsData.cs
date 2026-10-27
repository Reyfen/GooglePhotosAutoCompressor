using Newtonsoft.Json;

namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Progress of every item, saved to disk after each change so a run can resume.
    /// Saving is thread safe (download tasks save in parallel) and atomic (temp file + rename),
    /// so a crash never leaves a half-written state file behind.
    /// </summary>
    internal class FileItemsData
    {
        public static readonly string DEFAULT_PATH = Path.Combine(Const.DOWNLOADS_PATH, "GooglePhotosCache.json");

        public readonly HashSet<FileItem> Items;
        private readonly string _path;
        private readonly object _lock = new();

        public FileItemsData() : this(DEFAULT_PATH)
        {
        }

        public FileItemsData(string path)
        {
            _path = path;
            FileItem[]? items = Load(path) ?? Load(BackupPath);

            if (items == null && (File.Exists(path) || File.Exists(BackupPath)))
            {
                // Starting empty here would overwrite the user's progress on the next save.
                throw new InvalidDataException($"State file {path} and its backup are unreadable. Fix or delete them and run again.");
            }

            Items = new HashSet<FileItem>(items ?? Array.Empty<FileItem>());
        }

        private string BackupPath => _path + ".bak";

        /// <summary>Adds the item, or returns the stored item with the same media key.</summary>
        public FileItem AddOrGet(FileItem item)
        {
            lock (_lock)
            {
                if (Items.TryGetValue(item, out FileItem? stored))
                {
                    return stored;
                }

                Items.Add(item);
                return item;
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                string json = JsonConvert.SerializeObject(Items.ToArray(), Formatting.Indented);
                string tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, BackupPath);
                }
                else
                {
                    File.Move(tempPath, _path);
                }
            }
        }

        private static FileItem[]? Load(string path)
        {
            if (File.Exists(path) == false)
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<FileItem[]>(File.ReadAllText(path)) ?? Array.Empty<FileItem>();
            }
            catch (JsonException e)
            {
                Console.WriteLine($"State file {path} is unreadable ({e.Message}), trying backup.");
                return null;
            }
        }
    }
}
