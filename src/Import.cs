using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Vivid
{
    // Импорт обоев из Wallpaper Engine (папка projects\myprojects и мастерская Steam 431960)
    static class Import
    {
        public class Result { public int Added, Skipped, Scenes, Web; public List<string> SceneTitles = new List<string>(); }

        public static Result FromFiles(Library lib, IEnumerable<string> paths)
        {
            var r = new Result();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    if (File.Exists(Path.Combine(p, "project.json")) || Directory.EnumerateDirectories(p).Any(d => File.Exists(Path.Combine(d, "project.json"))))
                    {
                        var sub = FromWallpaperEngine(lib, new[] { p });
                        r.Added += sub.Added; r.Skipped += sub.Skipped; r.Scenes += sub.Scenes; r.Web += sub.Web; r.SceneTitles.AddRange(sub.SceneTitles);
                        continue;
                    }
                    foreach (var f in SafeFiles(p)) Add(lib, f, null, null, "file", r);
                }
                else Add(lib, p, null, null, "file", r);
            }
            return r;
        }

        static void Add(Library lib, string file, string title, string preview, string source, Result r)
        {
            if (Item.KindOf(file) == null) return;
            int before = lib.Items.Count;
            var it = lib.AddFile(file, title, preview, source);
            if (it != null && lib.Items.Count > before) r.Added++; else r.Skipped++;
        }

        static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => Item.KindOf(f) != null).ToList(); }
            catch { return new string[0]; }
        }

        // Где обычно лежат обои Wallpaper Engine
        public static List<string> GuessRoots()
        {
            var roots = new List<string>();
            var libs = new List<string>();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string sp = k == null ? null : k.GetValue("SteamPath") as string;
                    if (sp != null)
                    {
                        libs.Add(sp.Replace('/', '\\'));
                        string vdf = Path.Combine(sp, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (var line in File.ReadAllLines(vdf))
                            {
                                var t = line.Trim();
                                if (!t.StartsWith("\"path\"")) continue;
                                var parts = t.Split('"');
                                if (parts.Length >= 4) libs.Add(parts[3].Replace("\\\\", "\\"));
                            }
                    }
                }
            }
            catch { }
            foreach (var l in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(Path.Combine(l, "steamapps", "workshop", "content", "431960"));
                roots.Add(Path.Combine(l, "steamapps", "common", "wallpaper_engine", "projects", "myprojects"));
            }
            // отдельная (не Steam) установка: ищем рядом с рабочим столом
            try
            {
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                foreach (var d in Directory.EnumerateDirectories(desk))
                    if (File.Exists(Path.Combine(d, "wallpaper64.exe")) || File.Exists(Path.Combine(d, "wallpaper32.exe")))
                        roots.Add(Path.Combine(d, "projects", "myprojects"));
            }
            catch { }
            return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static Result FromWallpaperEngine(Library lib, IEnumerable<string> roots)
        {
            var r = new Result();
            var json = new JavaScriptSerializer();
            foreach (var root in roots)
            {
                IEnumerable<string> dirs;
                if (File.Exists(Path.Combine(root, "project.json"))) dirs = new[] { root };
                else
                {
                    // выбрали саму папку Wallpaper Engine — спускаемся в projects\myprojects
                    string mp = Path.Combine(root, "projects", "myprojects");
                    string start = Directory.Exists(mp) ? mp : root;
                    try { dirs = Directory.EnumerateDirectories(start).ToList(); } catch { continue; }
                }
                foreach (var d in dirs)
                {
                    string pj = Path.Combine(d, "project.json");
                    if (!File.Exists(pj)) continue;
                    try
                    {
                        var o = json.DeserializeObject(File.ReadAllText(pj, Encoding.UTF8)) as Dictionary<string, object>;
                        if (o == null) continue;
                        string type = (Str(o, "type") ?? "").ToLowerInvariant();
                        string title = Str(o, "title");
                        string file = Str(o, "file");
                        string prev = Str(o, "preview");
                        string prevPath = prev == null ? null : Path.Combine(d, prev);
                        if (type == "video" && file != null) Add(lib, Path.Combine(d, file), title, prevPath, "we", r);
                        else if (type == "scene")
                        {
                            string sp = Path.Combine(d, "scene.pkg");
                            if (!File.Exists(sp)) sp = Path.Combine(d, "scene.json");
                            if (File.Exists(sp)) Add(lib, sp, title, prevPath, "we", r);
                            else { r.Scenes++; r.SceneTitles.Add(title ?? Path.GetFileName(d)); }
                        }
                        else if (type == "web") r.Web++;
                        else if (file != null && Item.KindOf(file) != null) Add(lib, Path.Combine(d, file), title, prevPath, "we", r);
                    }
                    catch (Exception e) { Log.Write("project.json " + pj + ": " + e.Message); }
                }
            }
            return r;
        }

        static string Str(Dictionary<string, object> o, string k)
        {
            object v; return o.TryGetValue(k, out v) ? v as string : null;
        }
    }
}
