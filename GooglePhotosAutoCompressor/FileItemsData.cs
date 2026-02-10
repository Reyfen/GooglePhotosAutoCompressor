using Newtonsoft.Json;

namespace GooglePhotosAutoCompressor
{
    internal class FileItemsData
    {
        public readonly HashSet<FileItem> Items;
        private readonly string _path;

        public FileItemsData()
        {
            _path = Path.Combine(Const.DOWNLOADS_PATH, "GooglePhotosCache.json");

            if (File.Exists(_path) == false)
            {
                Items = new HashSet<FileItem>();
                return;
            }

            string json = File.ReadAllText(_path);
            Items = new (JsonConvert.DeserializeObject<FileItem[]>(json));
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(Items, Formatting.Indented);
            File.WriteAllText(_path, json);
        }
    }
}
