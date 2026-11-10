using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Backdrop
{
    // Превью для карточек: кадр из видео / первый кадр GIF / уменьшенная картинка. Кэшируются в %APPDATA%\Backdrop\thumbs.
    static class Thumbs
    {
        const int W = 480, H = 270;
        static readonly Queue<KeyValuePair<Item, Action<ImageSource>>> queue = new Queue<KeyValuePair<Item, Action<ImageSource>>>();
        static bool busy;

        static string CachePath(Item it)
        {
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(it.Path).Ticks; } catch { }
            return Path.Combine(Store.ThumbDir, it.Id + "_" + (stamp % 100000000).ToString() + ".jpg");
        }

        public static void Get(Item it, Action<ImageSource> done)
        {
            if (!it.Exists) { done(Fallback(it)); return; }
            string cp = CachePath(it);
            if (File.Exists(cp)) { var b = LoadFile(cp, W); if (b != null) { done(b); return; } }
            if (it.Kind == "scene")
            {
                var b = RenderScene(it.Path, W, H);
                if (b != null) { Save(b, cp); done(b); } else done(Fallback(it));
                return;
            }
            if (it.Kind != "video")
            {
                var b = LoadFile(it.Path, W);
                if (b != null) { Save(b, cp); done(b); }
                else done(Fallback(it));
                return;
            }
            queue.Enqueue(new KeyValuePair<Item, Action<ImageSource>>(it, done));
            Pump();
        }

        // Кадр сцены целиком (фон + частицы + надписи)
        public static BitmapSource RenderScene(string path, int w, int h)
        {
            SceneView sv = null;
            try
            {
                sv = new SceneView(path, Stretch.UniformToFill);
                if (sv.Error != null) return null;
                sv.Measure(new Size(w, h));
                sv.Arrange(new Rect(0, 0, w, h));
                sv.UpdateLayout();
                sv.Measure(new Size(w, h)); sv.Arrange(new Rect(0, 0, w, h)); sv.UpdateLayout(); // надписи встают на место после первого прохода
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(sv);
                rtb.Freeze();
                return rtb;
            }
            catch (Exception e) { Log.Write("Превью сцены: " + e.Message); return null; }
            finally { if (sv != null) sv.Dispose(); }
        }

        static ImageSource Fallback(Item it)
        {
            if (!string.IsNullOrEmpty(it.Preview) && File.Exists(it.Preview)) return LoadFile(it.Preview, W);
            return null;
        }

        static BitmapSource LoadFile(string path, int width)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = width;
                bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        static void Save(BitmapSource b, string path)
        {
            try
            {
                Directory.CreateDirectory(Store.ThumbDir);
                var enc = new JpegBitmapEncoder { QualityLevel = 88 };
                enc.Frames.Add(BitmapFrame.Create(b));
                using (var fs = File.Create(path)) enc.Save(fs);
            }
            catch (Exception e) { Log.Write(e); }
        }

        static void Pump()
        {
            if (busy || queue.Count == 0) return;
            busy = true;
            var job = queue.Dequeue();
            var it = job.Key;
            var mp = new MediaPlayer { IsMuted = true, ScrubbingEnabled = true, Volume = 0 };
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            bool finished = false;
            Action<ImageSource> finish = delegate(ImageSource img)
            {
                if (finished) return;
                finished = true; guard.Stop();
                try { mp.Close(); } catch { }
                job.Value(img ?? Fallback(it));
                busy = false;
                Pump();
            };
            guard.Tick += delegate { finish(null); };
            mp.MediaFailed += delegate { finish(null); };
            mp.MediaOpened += delegate
            {
                double dur = mp.NaturalDuration.HasTimeSpan ? mp.NaturalDuration.TimeSpan.TotalSeconds : 0;
                mp.Position = TimeSpan.FromSeconds(dur > 3 ? Math.Min(dur / 3, 6) : 0.2);
                mp.Pause();
                var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                wait.Tick += delegate
                {
                    wait.Stop();
                    try
                    {
                        int vw = Math.Max(1, mp.NaturalVideoWidth), vh = Math.Max(1, mp.NaturalVideoHeight);
                        double k = Math.Max((double)W / vw, (double)H / vh);
                        double dw = vw * k, dh = vh * k;
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, W, H));
                            dc.DrawVideo(mp, new Rect((W - dw) / 2, (H - dh) / 2, dw, dh));
                        }
                        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(dv); rtb.Freeze();
                        Save(rtb, CachePath(it));
                        finish(rtb);
                    }
                    catch (Exception e) { Log.Write(e); finish(null); }
                };
                wait.Start();
            };
            guard.Start();
            mp.Open(new Uri(it.Path));
        }
    }
}
