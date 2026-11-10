using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Vivid
{
    // Проигрыватель GIF: кадры собираются один раз (с учётом смещений и способа очистки), потом просто листаются.
    class GifAnimator
    {
        readonly Image target;
        readonly List<BitmapSource> frames = new List<BitmapSource>();
        readonly List<int> delays = new List<int>();
        readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);
        int index;
        public bool Ok { get { return frames.Count > 0; } }
        public List<BitmapSource> Frames { get { return frames; } }
        public List<int> Delays { get { return delays; } }

        public GifAnimator(Image target, string path, int maxWidth)
        {
            this.target = target;
            timer.Tick += Next;
            try { Decode(path, maxWidth); }
            catch (Exception e) { Log.Write("GIF " + path + ": " + e.Message); }
            if (frames.Count > 0) target.Source = frames[0];
        }

        static T Q<T>(BitmapMetadata m, string q, T def)
        {
            try { object o = m.GetQuery(q); if (o is T) return (T)o; if (o != null) return (T)Convert.ChangeType(o, typeof(T)); }
            catch { }
            return def;
        }

        void Decode(string path, int maxWidth)
        {
            var dec = new GifBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count == 0) return;
            int w = dec.Frames[0].PixelWidth, h = dec.Frames[0].PixelHeight;
            try
            {
                var gm = dec.Metadata as BitmapMetadata;
                if (gm != null) { w = Q<ushort>(gm, "/logscrdesc/Width", (ushort)w); h = Q<ushort>(gm, "/logscrdesc/Height", (ushort)h); }
            }
            catch { }
            if (w <= 0 || h <= 0) return;

            // Ограничиваем память: не больше ~500 МБ на все кадры и не шире экрана
            double scale = 1;
            if (maxWidth > 0 && w > maxWidth) scale = (double)maxWidth / w;
            long bytes = (long)(w * scale) * (long)(h * scale) * 4 * dec.Frames.Count;
            if (bytes > 500L * 1024 * 1024) scale *= Math.Sqrt(500.0 * 1024 * 1024 / bytes);
            int cw = Math.Max(1, (int)(w * scale)), ch = Math.Max(1, (int)(h * scale));

            RenderTargetBitmap prev = null, beforeFrame = null;
            int lastDisposal = 0; Rect lastRect = Rect.Empty;
            for (int i = 0; i < dec.Frames.Count; i++)
            {
                var f = dec.Frames[i];
                var md = f.Metadata as BitmapMetadata;
                int left = 0, top = 0, delay = 10, disposal = 0;
                if (md != null)
                {
                    left = Q<ushort>(md, "/imgdesc/Left", 0);
                    top = Q<ushort>(md, "/imgdesc/Top", 0);
                    delay = Q<ushort>(md, "/grctlext/Delay", 10);
                    disposal = Q<byte>(md, "/grctlext/Disposal", 0);
                }
                if (delay < 2) delay = 10; // как в браузерах: 0–1 считаются 100 мс
                var rect = new Rect(left * scale, top * scale, f.PixelWidth * scale, f.PixelHeight * scale);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    BitmapSource baseImg = lastDisposal == 3 ? beforeFrame : prev;
                    if (baseImg != null)
                    {
                        // способ очистки 2: область прошлого кадра становится прозрачной
                        bool clear = lastDisposal == 2 && !lastRect.IsEmpty;
                        if (clear) dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, cw, ch)), new RectangleGeometry(lastRect)));
                        dc.DrawImage(baseImg, new Rect(0, 0, cw, ch));
                        if (clear) dc.Pop();
                    }
                    dc.DrawImage(f, rect);
                }
                var rtb = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();

                if (disposal == 3) beforeFrame = prev; // «вернуть предыдущее»
                frames.Add(rtb);
                delays.Add(delay * 10);
                prev = rtb; lastDisposal = disposal; lastRect = rect;
            }
        }

        void Next(object s, EventArgs e)
        {
            if (frames.Count < 2) return;
            index = (index + 1) % frames.Count;
            target.Source = frames[index];
            timer.Interval = TimeSpan.FromMilliseconds(delays[index]);
        }

        public void Play()
        {
            if (frames.Count < 2 || timer.IsEnabled) return;
            timer.Interval = TimeSpan.FromMilliseconds(delays[index]);
            timer.Start();
        }

        public void Pause() { timer.Stop(); }

        public void Dispose() { timer.Stop(); frames.Clear(); target.Source = null; }
    }
}
