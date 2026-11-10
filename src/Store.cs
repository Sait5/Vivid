using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Vivid
{
    public class Item
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public string Kind { get; set; }      // video | gif | image
        public string Preview { get; set; }   // картинка-превью (необязательно)
        public string Source { get; set; }    // file | we
        public long Added { get; set; }
        public bool Exists { get { return !string.IsNullOrEmpty(Path) && File.Exists(Path); } }

        public string KindLabel
        {
            get { return Kind == "video" ? "ВИДЕО" : Kind == "gif" ? "GIF" : Kind == "scene" ? "СЦЕНА" : "ФОТО"; }
        }

        public static string KindOf(string path)
        {
            string e = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            string n = (System.IO.Path.GetFileName(path) ?? "").ToLowerInvariant();
            if (n == "scene.pkg" || n == "scene.json") return "scene";
            switch (e)
            {
                case ".mp4": case ".m4v": case ".webm": case ".mkv": case ".mov": case ".avi": case ".wmv": return "video";
                case ".gif": return "gif";
                case ".jpg": case ".jpeg": case ".png": case ".bmp": case ".webp": case ".tif": case ".tiff": case ".jfif": return "image";
            }
            return null;
        }
    }

    public class Settings
    {
        public bool PauseFullscreen { get; set; }
        public bool PauseMaximized { get; set; }
        public bool PauseFocused { get; set; }
        public bool SameOnAll { get; set; }
        public bool Paused { get; set; }
        public int Volume { get; set; }          // 0..100, 0 = без звука
        public string Fit { get; set; }          // fill | fit | stretch
        public string Current { get; set; }      // обои для всех мониторов
        public Dictionary<string, string> PerMonitor { get; set; }
        public int Speed { get; set; }           // скорость видео, % (100 = обычная)
        public string Accent { get; set; }       // акцентный цвет интерфейса, #RRGGBB
        public bool ShowOnStartup { get; set; }  // открывать окно при автозапуске вместе с Windows (по умолчанию нет — только трей)

        public Settings()
        {
            Accent = "#FF6B4A";
            PauseFullscreen = true; PauseMaximized = true; PauseFocused = false;
            SameOnAll = true; Volume = 0; Fit = "fill"; Speed = 100;
            PerMonitor = new Dictionary<string, string>();
        }

        public string For(string monitor)
        {
            string id;
            if (!SameOnAll && PerMonitor != null && monitor != null && PerMonitor.TryGetValue(monitor, out id)) return id;
            return Current;
        }

        public void Save() { Store.Save("settings.json", this); }
        public static Settings Load() { return Store.Load<Settings>("settings.json") ?? new Settings(); }
    }

    public class Library
    {
        public List<Item> Items { get; set; }
        public Library() { Items = new List<Item>(); }

        public Item Get(string id)
        {
            if (id == null) return null;
            return Items.FirstOrDefault(i => i.Id == id);
        }

        public Item AddFile(string path, string title, string preview, string source)
        {
            string kind = Item.KindOf(path);
            if (kind == null || !File.Exists(path)) return null;
            string full = System.IO.Path.GetFullPath(path);
            var have = Items.FirstOrDefault(i => string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase));
            if (have != null) return have;
            var it = new Item
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 12),
                Title = string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileNameWithoutExtension(full) : title.Trim(),
                Path = full, Kind = kind, Preview = preview, Source = source ?? "file",
                Added = DateTime.UtcNow.Ticks
            };
            Items.Add(it);
            return it;
        }

        public void Save() { Store.Save("library.json", this); }
        public static Library Load() { return Store.Load<Library>("library.json") ?? new Library(); }
    }

    static class Store
    {
        public static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vivid");
        public static string ThumbDir { get { return System.IO.Path.Combine(Dir, "thumbs"); } }

        static JavaScriptSerializer Json() { var j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }

        public static void Save(string name, object o)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string p = System.IO.Path.Combine(Dir, name), tmp = p + ".tmp";
                File.WriteAllText(tmp, Json().Serialize(o), new UTF8Encoding(false));
                if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            }
            catch (Exception e) { Log.Write(e); }
        }

        public static T Load<T>(string name) where T : class
        {
            try
            {
                string p = System.IO.Path.Combine(Dir, name);
                if (!File.Exists(p)) return null;
                return Json().Deserialize<T>(File.ReadAllText(p, Encoding.UTF8));
            }
            catch (Exception e) { Log.Write(e); return null; }
        }
    }

    static class Log
    {
        public static void Write(object o)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                File.AppendAllText(System.IO.Path.Combine(Store.Dir, "log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + o + Environment.NewLine);
            }
            catch { }
        }
    }
}
