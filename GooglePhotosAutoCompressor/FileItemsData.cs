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
        /// <summary>Kept in the app's Downloads folder whatever the work folder is (moved there from Downloads by <see cref="AppFolderMigration"/>).</summary>
        public static readonly string DEFAULT_PATH = Path.Combine(Const.APP_DOWNLOADS_PATH, "progress.json");

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

        /// <summary>Raised after every save, so the UI can refresh its counts.</summary>
        public event Action? Saved;

        public void Save()
        {
            lock (_lock)
            {
                string json = JsonConvert.SerializeObject(Items.ToArray(), Formatting.Indented);
                DurableFile.WriteAllText(_path, json, BackupPath);
            }

            Saved?.Invoke();
        }

        /// <summary>Copy of the items, safe to read while download tasks are saving.</summary>
        public FileItem[] Snapshot()
        {
            lock (_lock)
            {
                return Items.ToArray();
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
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    // A power cut can leave an empty file. Treating it as "no items" would lose all progress.
                    Console.WriteLine($"State file {path} is empty, trying backup.");
                    return null;
                }

                return JsonConvert.DeserializeObject<FileItem[]>(json);
            }
            catch (JsonException e)
            {
                Console.WriteLine($"State file {path} is unreadable ({e.Message}), trying backup.");
                return null;
            }
        }
    }
}
