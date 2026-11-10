using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Backdrop
{
    // Файлы сцены: scene.pkg (архив WE), распакованная папка проекта и общие ассеты Wallpaper Engine.
    class ScenePackage
    {
        readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        public string ProjectDir, AssetsDir, Id;

        public static ScenePackage Open(string path)
        {
            var p = new ScenePackage();
            p.ProjectDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            p.Id = Math.Abs(p.ProjectDir.ToLowerInvariant().GetHashCode()).ToString("x");
            string pkg = Path.Combine(p.ProjectDir, "scene.pkg");
            if (File.Exists(pkg)) p.ReadPkg(pkg);
            p.AssetsDir = FindAssets(p.ProjectDir);
            return p;
        }

        void ReadPkg(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            using (var r = new BinaryReader(new MemoryStream(b)))
            {
                Func<string> str = delegate { int n = r.ReadInt32(); return Encoding.UTF8.GetString(r.ReadBytes(n)); };
                string ver = str();
                if (!ver.StartsWith("PKGV")) throw new InvalidDataException("не архив сцены");
                int count = r.ReadInt32();
                var list = new List<Tuple<string, int, int>>();
                for (int i = 0; i < count; i++) { string n = str(); int off = r.ReadInt32(); int len = r.ReadInt32(); list.Add(Tuple.Create(n, off, len)); }
                long baseOff = r.BaseStream.Position;
                foreach (var e in list)
                {
                    var data = new byte[e.Item3];
                    Buffer.BlockCopy(b, (int)(baseOff + e.Item2), data, 0, e.Item3);
                    files[e.Item1.Replace('\\', '/')] = data;
                }
            }
        }

        // Ищем папку assets самого Wallpaper Engine (нужна для стандартных частиц, шрифтов и т.д.)
        static string FindAssets(string dir)
        {
            try
            {
                var d = new DirectoryInfo(dir);
                for (int i = 0; i < 5 && d != null; i++, d = d.Parent)
                {
                    string a = Path.Combine(d.FullName, "assets");
                    if (Directory.Exists(Path.Combine(a, "materials"))) return a;
                }
                string low = dir.ToLowerInvariant();
                int k = low.IndexOf(@"\steamapps\workshop\");
                if (k > 0)
                {
                    string a = Path.Combine(dir.Substring(0, k), @"steamapps\common\wallpaper_engine\assets");
                    if (Directory.Exists(a)) return a;
                }
            }
            catch { }
            return null;
        }

        public byte[] Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace('\\', '/');
            byte[] b;
            if (files.TryGetValue(name, out b)) return b;
            foreach (var root in new[] { ProjectDir, AssetsDir })
            {
                if (root == null) continue;
                string f = Path.Combine(root, name.Replace('/', '\\'));
                if (File.Exists(f)) return File.ReadAllBytes(f);
            }
            return null;
        }

        public Dictionary<string, object> Json(string name)
        {
            var b = Get(name);
            if (b == null) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(Encoding.UTF8.GetString(b).TrimStart('﻿')) as Dictionary<string, object>; }
            catch { return null; }
        }

        // Сохраняет файл на диск (шрифтам WPF нужен настоящий путь)
        public string Extract(string name)
        {
            var b = Get(name);
            if (b == null) return null;
            string dir = Path.Combine(Store.Dir, "cache", Id);
            string f = Path.Combine(dir, name.Replace('/', '\\'));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                if (!File.Exists(f) || new FileInfo(f).Length != b.Length) File.WriteAllBytes(f, b);
                return f;
            }
            catch { return null; }
        }
    }

    // Разбор значений из scene.json (числа, строки "1 2 3", {value: ...})
    static class J
    {
        public static object Raw(Dictionary<string, object> o, string k)
        {
            object v;
            if (o == null || !o.TryGetValue(k, out v)) return null;
            var d = v as Dictionary<string, object>;
            if (d != null && d.ContainsKey("value")) return d["value"];
            return v;
        }

        public static double Num(Dictionary<string, object> o, string k, double def)
        {
            return ToNum(Raw(o, k), def);
        }

        public static double ToNum(object v, double def)
        {
            if (v == null) return def;
            if (v is int) return (int)v;
            if (v is decimal) return (double)(decimal)v;
            if (v is double) return (double)v;
            if (v is long) return (long)v;
            if (v is bool) return (bool)v ? 1 : 0;
            double r;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        public static double[] Vec(Dictionary<string, object> o, string k, double[] def)
        {
            return ToVec(Raw(o, k), def);
        }

        public static double[] ToVec(object v, double[] def)
        {
            if (v == null) return def;
            if (!(v is string)) { double n = ToNum(v, double.NaN); return double.IsNaN(n) ? def : new[] { n, n, n }; }
            var parts = ((string)v).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var r = new double[Math.Max(3, parts.Length)];
            for (int i = 0; i < parts.Length; i++) r[i] = ToNum(parts[i], 0);
            return r;
        }

        public static bool Bool(Dictionary<string, object> o, string k, bool def)
        {
            var v = Raw(o, k);
            if (v == null) return def;
            if (v is bool) return (bool)v;
            return ToNum(v, def ? 1 : 0) != 0;
        }

        public static string Str(Dictionary<string, object> o, string k)
        {
            var v = Raw(o, k);
            return v as string;
        }

        public static List<Dictionary<string, object>> List(Dictionary<string, object> o, string k)
        {
            object v;
            var res = new List<Dictionary<string, object>>();
            if (o == null || !o.TryGetValue(k, out v) || v == null) return res;
            var arr = v as object[];
            if (arr == null) { var al = v as System.Collections.ArrayList; if (al != null) arr = al.ToArray(); }
            if (arr == null) return res;
            foreach (var x in arr) { var d = x as Dictionary<string, object>; if (d != null) res.Add(d); }
            return res;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> o, string k)
        {
            object v;
            return o != null && o.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }
    }

    // Живая сцена Wallpaper Engine, нарисованная средствами WPF
    class SceneView : Grid
    {
        public double W = 1920, H = 1080;
        readonly Canvas canvas = new Canvas();
        readonly Viewbox box = new Viewbox();
        readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        readonly List<TextLayer> texts = new List<TextLayer>();
        ScriptHost js;
        readonly DispatcherTimer textTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        DateTime last;
        bool playing;
        public string Error;
        public BitmapSource Poster; // самая большая картинка сцены — для превью
        public Canvas Canvas { get { return canvas; } }
        public List<ParticleSystem> ParticleSystems { get { return particles; } }
        public List<TextLayer> TextLayers { get { return texts; } }
        public void RefreshTexts() { UpdateTexts(); }

        // Множитель размера шрифта (pointsize → пиксели сцены), подобран по превью WE
        public const double FontScale = 3.75;

        public SceneView(string path, Stretch stretch)
        {
            Background = Brushes.Black;
            ClipToBounds = true;
            box.Stretch = stretch;
            box.Child = canvas;
            Children.Add(box);
            textTimer.Tick += delegate { UpdateTexts(); };
            try { Build(path); }
            catch (Exception e) { Error = e.Message; Log.Write("Сцена " + path + ": " + e); }
        }

        public Stretch Stretch { get { return box.Stretch; } set { box.Stretch = value; } }

        void Build(string path)
        {
            var pkg = ScenePackage.Open(path);
            var scene = pkg.Json("scene.json");
            if (scene == null) throw new InvalidDataException("в сцене нет scene.json");
            var general = J.Obj(scene, "general");
            var ortho = J.Obj(general, "orthogonalprojection");
            if (ortho != null) { W = J.Num(ortho, "width", W); H = J.Num(ortho, "height", H); }
            canvas.Width = W; canvas.Height = H;
            var clear = J.Vec(general, "clearcolor", new double[] { 0, 0, 0 });
            canvas.Background = new SolidColorBrush(Color.FromRgb(C(clear[0]), C(clear[1]), C(clear[2])));

            foreach (var obj in J.List(scene, "objects"))
            {
                if (!J.Bool(obj, "visible", true)) continue;
                try
                {
                    if (obj.ContainsKey("image")) AddImage(pkg, obj);
                    else if (obj.ContainsKey("particle")) AddParticles(pkg, obj);
                    else if (obj.ContainsKey("text")) AddText(pkg, obj);
                }
                catch (Exception e) { Log.Write("Слой «" + J.Str(obj, "name") + "»: " + e.Message); }
            }
            UpdateTexts();
            foreach (var p in particles) p.Prewarm();
            foreach (var p in particles) p.Render();
        }

        static byte C(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255))); }

        Point ToCanvas(double[] origin) { return new Point(origin[0], H - origin[1]); }

        TexImage LoadTexture(ScenePackage pkg, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var data = pkg.Get("materials/" + name + ".tex") ?? pkg.Get(name + ".tex") ?? pkg.Get(name);
            return data == null ? null : TexImage.Load(data);
        }

        string MaterialTexture(ScenePackage pkg, string materialPath)
        {
            var mat = pkg.Json(materialPath);
            var pass = J.List(mat, "passes").FirstOrDefault();
            if (pass == null) return null;
            object t;
            if (!pass.TryGetValue("textures", out t)) return null;
            var arr = t as object[];
            if (arr == null) { var al = t as System.Collections.ArrayList; if (al != null) arr = al.ToArray(); }
            return arr != null && arr.Length > 0 ? arr[0] as string : null;
        }

        void AddImage(ScenePackage pkg, Dictionary<string, object> obj)
        {
            var model = pkg.Json(J.Str(obj, "image"));
            string matPath = J.Str(model, "material");
            if (matPath == null) return;
            var tex = LoadTexture(pkg, MaterialTexture(pkg, matPath));
            if (tex == null || tex.Bitmap == null) return;
            var origin = J.Vec(obj, "origin", new[] { W / 2, H / 2, 0 });
            var size = J.Vec(obj, "size", new double[] { tex.Width, tex.Height, 0 });
            if (J.Bool(model, "fullscreen", false)) { size = new[] { W, H, 0 }; origin = new[] { W / 2, H / 2, 0 }; }
            var scale = J.Vec(obj, "scale", new double[] { 1, 1, 1 });
            var angles = J.Vec(obj, "angles", new double[] { 0, 0, 0 });
            double w = size[0] * scale[0], h = size[1] * scale[1];
            var img = new Image { Source = tex.Bitmap, Width = Math.Abs(w), Height = Math.Abs(h), Stretch = Stretch.Fill, IsHitTestVisible = false };
            img.Opacity = J.Num(obj, "alpha", 1);
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var p = ToCanvas(origin);
            Canvas.SetLeft(img, p.X - Math.Abs(w) / 2); Canvas.SetTop(img, p.Y - Math.Abs(h) / 2);
            if (angles[2] != 0 || w < 0 || h < 0)
            {
                var tg = new TransformGroup();
                tg.Children.Add(new ScaleTransform(w < 0 ? -1 : 1, h < 0 ? -1 : 1, Math.Abs(w) / 2, Math.Abs(h) / 2));
                tg.Children.Add(new RotateTransform(-angles[2] * 180 / Math.PI, Math.Abs(w) / 2, Math.Abs(h) / 2));
                img.RenderTransform = tg;
            }
            canvas.Children.Add(img);
            if (Poster == null || tex.Width * tex.Height > Poster.PixelWidth * Poster.PixelHeight) Poster = tex.Bitmap;
        }

        void AddParticles(ScenePackage pkg, Dictionary<string, object> obj)
        {
            var def = pkg.Json(J.Str(obj, "particle"));
            if (def == null) return;
            var origin = J.Vec(obj, "origin", new[] { W / 2, H / 2, 0 });
            var scale = J.Vec(obj, "scale", new double[] { 1, 1, 1 });
            string texName = MaterialTexture(pkg, J.Str(def, "material"));
            TexImage tex = null;
            try { tex = LoadTexture(pkg, texName); } catch (Exception e) { Log.Write("Текстура частиц " + texName + ": " + e.Message); }
            var sheet = pkg.Json("materials/" + texName + ".tex-json");
            var ps = new ParticleSystem(def, J.Obj(obj, "instanceoverride"), origin, scale, tex, sheet, W, H);
            canvas.Children.Add(ps.Host);
            particles.Add(ps);
        }

        void AddText(ScenePackage pkg, Dictionary<string, object> obj)
        {
            var tl = new TextLayer();
            object tv; obj.TryGetValue("text", out tv);
            var td = tv as Dictionary<string, object>;
            tl.Value = td != null ? Convert.ToString(td.ContainsKey("value") ? td["value"] : "") : Convert.ToString(tv);
            string script = td == null ? null : J.Str(td, "script");
            var props = td == null ? null : J.Obj(td, "scriptproperties");

            var origin = J.Vec(obj, "origin", new[] { W / 2, H / 2, 0 });
            var scale = J.Vec(obj, "scale", new double[] { 1, 1, 1 });
            double pt = J.Num(obj, "pointsize", 32);
            var color = J.Vec(obj, "color", new double[] { 1, 1, 1 });
            double brightness = J.Num(obj, "brightness", 1), alpha = J.Num(obj, "alpha", 1);
            string ha = J.Str(obj, "horizontalalign") ?? "center", va = J.Str(obj, "verticalalign") ?? "center";

            FontFamily family = new FontFamily("Segoe UI");
            string fontPath = J.Str(obj, "font");
            if (fontPath != null && !fontPath.StartsWith("systemfont"))
            {
                string f = pkg.Extract(fontPath);
                if (f != null)
                {
                    try
                    {
                        // WPF сам читает имя семейства из файла шрифта
                        var fams = Fonts.GetFontFamilies(f);
                        var fam0 = fams.FirstOrDefault();
                        if (fam0 != null) family = fam0;
                    }
                    catch (Exception e) { Log.Write("Шрифт " + fontPath + ": " + e.Message); }
                }
            }
            else if (fontPath != null) family = new FontFamily(fontPath.Replace("systemfont_", ""));

            double sy = Math.Abs(scale[1]);
            var fg = new SolidColorBrush(Color.FromArgb(C(alpha), C(color[0] * Math.Min(1, brightness)), C(color[1] * Math.Min(1, brightness)), C(color[2] * Math.Min(1, brightness))));
            fg.Freeze();
            Func<TextBlock> make = delegate
            {
                return new TextBlock
                {
                    FontFamily = family, FontSize = Math.Max(1, pt * FontScale * sy), Foreground = fg, IsHitTestVisible = false,
                    TextAlignment = ha == "left" ? TextAlignment.Left : ha == "right" ? TextAlignment.Right : TextAlignment.Center
                };
            };
            var host = new Grid();
            bool blur = J.List(obj, "effects").Any(e => (J.Str(e, "file") ?? "").IndexOf("blur", StringComparison.OrdinalIgnoreCase) >= 0 && J.Bool(e, "visible", true));
            if (blur || brightness > 1.05)
            {
                // свечение: размытая копия под текстом (так WE-сцены получают «неоновый» вид)
                var glow = make();
                glow.Effect = new BlurEffect { Radius = Math.Max(4, pt * FontScale * sy * 0.18), KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
                glow.Opacity = Math.Min(1, 0.35 + 0.25 * brightness);
                tl.Blocks.Add(glow); host.Children.Add(glow);
            }
            var main = make();
            tl.Blocks.Add(main); host.Children.Add(main);
            // запас по краям, чтобы свечение не обрезалось при отрисовке в картинку
            double pad = Math.Ceiling(Math.Max(4, pt * FontScale * sy * 0.18) * 1.6);
            foreach (var tb in tl.Blocks) tb.Margin = new Thickness(pad);
            host.CacheMode = new BitmapCache(1);
            tl.Host = host;
            tl.Origin = ToCanvas(origin); tl.HAlign = ha; tl.VAlign = va;
            canvas.Children.Add(host);
            host.SizeChanged += delegate { tl.Place(); };

            if (script != null)
            {
                if (js == null) js = new ScriptHost();
                tl.Layer = js.Add(script, props);
                if (tl.Layer != null)
                {
                    var init = js.Call(tl.Layer, "init", tl.Value);
                    if (init != null) tl.Value = init;
                }
            }
            texts.Add(tl);
        }

        void UpdateTexts()
        {
            foreach (var t in texts)
            {
                if (t.Layer != null)
                {
                    var v = js.Call(t.Layer, "update", t.Value);
                    if (v != null) t.Value = v;
                }
                t.Show();
            }
        }

        public void Play()
        {
            if (playing) return;
            playing = true;
            last = DateTime.UtcNow;
            if (particles.Count > 0) CompositionTarget.Rendering += OnFrame;
            if (texts.Count > 0) { UpdateTexts(); textTimer.Start(); }
        }

        public void Pause()
        {
            if (!playing) return;
            playing = false;
            CompositionTarget.Rendering -= OnFrame;
            textTimer.Stop();
        }

        TimeSpan lastRender;
        void OnFrame(object s, EventArgs e)
        {
            var re = e as RenderingEventArgs;
            if (re != null) { if (re.RenderingTime == lastRender) return; lastRender = re.RenderingTime; }
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.1, (now - last).TotalSeconds);
            last = now;
            foreach (var p in particles) { p.Step(dt); p.Render(); }
        }

        public void Dispose()
        {
            Pause();
            if (js != null) { js.Dispose(); js = null; }
            canvas.Children.Clear();
        }

        public class TextLayer
        {
            public string Value, Layer, HAlign, VAlign;
            public Point Origin;
            public Grid Host;
            public readonly List<TextBlock> Blocks = new List<TextBlock>();
            public int Version;
            string shown;

            public void Show()
            {
                if (Value == shown) return;
                shown = Value;
                Version++;
                foreach (var b in Blocks) b.Text = Value ?? "";
            }

            public void Place()
            {
                double w = Host.ActualWidth, h = Host.ActualHeight;
                double x = HAlign == "left" ? Origin.X : HAlign == "right" ? Origin.X - w : Origin.X - w / 2;
                double y = VAlign == "top" ? Origin.Y : VAlign == "bottom" ? Origin.Y - h : Origin.Y - h / 2;
                Canvas.SetLeft(Host, x); Canvas.SetTop(Host, y);
            }
        }
    }
}
