using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Composition;
using WMedia = System.Windows.Media;

namespace Backdrop
{
    // Системный композитор Windows (тот же механизм, что у современных приложений).
    // Только его содержимое видно на новом рабочем столе Windows 11, а анимации он крутит сам — почти без нагрузки на процессор.
    static class Comp
    {
        [StructLayout(LayoutKind.Sequential)]
        struct DQO { public int dwSize, threadType, apartmentType; }
        [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DQO o, out IntPtr ctrl);

        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out IntPtr target); }

        static IntPtr queue;
        static Compositor compositor;

        public static Compositor C
        {
            get
            {
                if (compositor == null)
                {
                    if (queue == IntPtr.Zero) CreateDispatcherQueueController(new DQO { dwSize = 12, threadType = 2, apartmentType = 2 }, out queue);
                    compositor = new Compositor();
                }
                return compositor;
            }
        }

        public static Windows.UI.Composition.Desktop.DesktopWindowTarget Target(IntPtr hwnd)
        {
            IntPtr t;
            ((ICompositorDesktopInterop)(object)C).CreateDesktopWindowTarget(hwnd, false, out t);
            var target = (Windows.UI.Composition.Desktop.DesktopWindowTarget)Marshal.GetObjectForIUnknown(t);
            Marshal.Release(t);
            return target;
        }

        // ---- загрузка картинок прямо в видеокарту (Direct3D 11 → поверхность композитора) ----
        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint numLevels, uint sdk, out IntPtr device, out int level, out IntPtr context);

        [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICompositorInterop
        {
            void CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr surface);
            void CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr surface);
            void CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr device);
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct BOX { public uint Left, Top, Front, Right, Bottom, Back; }

        [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ICompositionDrawingSurfaceInterop
        {
            void BeginDraw(IntPtr updateRect, ref Guid iid, out IntPtr updateObject, out POINT offset);
            void EndDraw();
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void UpdateSubresourceFn(IntPtr ctx, IntPtr dst, uint sub, ref BOX box, IntPtr data, uint rowPitch, uint depthPitch);

        static IntPtr d3dDevice, d3dContext;
        static CompositionGraphicsDevice gfx;
        static UpdateSubresourceFn updateSubresource;
        static Guid IID_Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        static CompositionGraphicsDevice Gfx
        {
            get
            {
                if (gfx != null) return gfx;
                int lvl;
                int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3dDevice, out lvl, out d3dContext);
                if (hr < 0) hr = D3D11CreateDevice(IntPtr.Zero, 5, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3dDevice, out lvl, out d3dContext); // WARP
                if (hr < 0) throw new COMException("Direct3D 11 недоступен", hr);
                IntPtr vtbl = Marshal.ReadIntPtr(d3dContext);
                updateSubresource = (UpdateSubresourceFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtbl, 48 * IntPtr.Size), typeof(UpdateSubresourceFn));
                IntPtr g;
                ((ICompositorInterop)(object)C).CreateGraphicsDevice(d3dDevice, out g);
                gfx = (CompositionGraphicsDevice)Marshal.GetObjectForIUnknown(g);
                Marshal.Release(g);
                return gfx;
            }
        }

        public static CompositionDrawingSurface Surface(BitmapSource bmp)
        {
            var src = bmp.Format == System.Windows.Media.PixelFormats.Pbgra32 ? bmp : new FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            src.CopyPixels(px, stride, 0);
            var surf = Gfx.CreateDrawingSurface(new Windows.Foundation.Size(w, h),
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Windows.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            var interop = (ICompositionDrawingSurfaceInterop)(object)surf;
            IntPtr tex; POINT off;
            interop.BeginDraw(IntPtr.Zero, ref IID_Texture2D, out tex, out off);
            var handle = GCHandle.Alloc(px, GCHandleType.Pinned);
            try
            {
                var box = new BOX { Left = (uint)off.X, Top = (uint)off.Y, Front = 0, Right = (uint)(off.X + w), Bottom = (uint)(off.Y + h), Back = 1 };
                updateSubresource(d3dContext, tex, 0, ref box, handle.AddrOfPinnedObject(), (uint)stride, 0);
            }
            finally { handle.Free(); Marshal.Release(tex); interop.EndDraw(); }
            return surf;
        }

        // Картинка WPF → кисть композитора
        public static CompositionSurfaceBrush Brush(BitmapSource bmp, string dir) { return Brush(bmp, dir, false); }

        public static CompositionSurfaceBrush Brush(BitmapSource bmp, string dir, bool temp)
        {
            var b = C.CreateSurfaceBrush(Surface(bmp));
            b.Stretch = CompositionStretch.Fill;
            return b;
        }

        public static CompositionSurfaceBrush FileBrush(string file, int maxWidth)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(file);
            bi.EndInit();
            BitmapSource s = bi;
            if (maxWidth > 0 && bi.PixelWidth > maxWidth)
            {
                double k = (double)maxWidth / bi.PixelWidth;
                s = new TransformedBitmap(bi, new System.Windows.Media.ScaleTransform(k, k));
            }
            return Brush(s, null);
        }

        public static CompositionStretch StretchOf(string fit)
        {
            return fit == "fit" ? CompositionStretch.Uniform : fit == "stretch" ? CompositionStretch.Fill : CompositionStretch.UniformToFill;
        }
    }

    // Одно окно обоев на монитор: простое нативное окно внутри рабочего стола, содержимое рисует композитор.
    class WallpaperWindow
    {
        public readonly string Monitor;
        public System.Drawing.Rectangle Bounds;
        public Item Current { get; private set; }
        public bool IsPaused { get; private set; }
        public IntPtr Hwnd { get { return host == null ? IntPtr.Zero : host.Handle; } }

        Container host;
        Windows.UI.Composition.Desktop.DesktopWindowTarget target;
        ContainerVisual root;
        SpriteVisual background;
        IContent content;
        readonly string cacheDir;

        class Container : System.Windows.Forms.NativeWindow
        {
            public Container(IntPtr parent, System.Drawing.Rectangle b)
            {
                var cp = new System.Windows.Forms.CreateParams
                {
                    Caption = "Backdrop wallpaper", Parent = parent, Width = Math.Max(1, b.Width), Height = Math.Max(1, b.Height),
                    Style = Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPSIBLINGS,
                    ExStyle = Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW
                };
                CreateHandle(cp);
            }

            protected override void WndProc(ref System.Windows.Forms.Message m)
            {
                const int WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84;
                if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(3); return; }
                if (m.Msg == WM_NCHITTEST) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
                base.WndProc(ref m);
            }
        }

        public WallpaperWindow(string monitor, System.Drawing.Rectangle bounds)
        {
            Monitor = monitor; Bounds = bounds;
            cacheDir = System.IO.Path.Combine(Store.Dir, "cache", "live", Math.Abs(monitor.GetHashCode()).ToString("x"));
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
        }

        public bool Attach()
        {
            if (!DesktopHost.Valid && !DesktopHost.Find()) return false;
            if (IsAttached) { Move(Bounds); return true; }
            DropHost();
            host = new Container(DesktopHost.Host, Bounds);
            target = Comp.Target(host.Handle);
            root = Comp.C.CreateContainerVisual();
            root.Clip = Comp.C.CreateInsetClip();
            background = Comp.C.CreateSpriteVisual();
            background.Brush = Comp.C.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
            background.RelativeSizeAdjustment = new Vector2(1, 1);
            root.Children.InsertAtBottom(background);
            target.Root = root;
            Move(Bounds);
            if (content != null) { content.AttachTo(root); }
            return true;
        }

        void DropHost()
        {
            if (content != null && root != null) content.DetachFrom(root);
            if (target != null) { try { target.Root = null; target.Dispose(); } catch { } target = null; }
            if (root != null) { try { root.Dispose(); } catch { } root = null; }
            if (host != null) { try { host.DestroyHandle(); } catch { } host = null; }
        }

        public bool IsAttached { get { return host != null && DesktopHost.IsAttached(host.Handle); } }

        public void Move(System.Drawing.Rectangle b)
        {
            Bounds = b;
            if (host == null) return;
            DesktopHost.Place(host.Handle, b);
            if (root != null) root.Size = new Vector2(b.Width, b.Height);
            if (content != null) content.Resize(b.Width, b.Height);
        }

        public void Load(Item item, Settings cfg)
        {
            if (content != null) { if (root != null) content.DetachFrom(root); content.Dispose(); content = null; }
            Current = null;
            if (item == null || !item.Exists) return;
            Current = item;
            try
            {
                if (item.Kind == "video") content = new VideoContent(item.Path, cfg);
                else if (item.Kind == "scene") content = new SceneContent(item.Path, cfg, cacheDir);
                else if (item.Kind == "gif") content = new GifContent(item.Path, cfg, cacheDir, Bounds.Width);
                else content = new ImageContent(item.Path, cfg);
                content.Resize(Bounds.Width, Bounds.Height);
                if (root != null) content.AttachTo(root);
                if (!IsPaused) content.Play();
            }
            catch (Exception e) { Log.Write("Не удалось загрузить " + item.Path + ": " + e); }
        }

        public void ApplyAudio(Settings cfg) { if (content != null) content.Apply(cfg); }
        public void ApplySpeed(Settings cfg) { if (content != null) content.Apply(cfg); }
        public void ApplyFit(Settings cfg) { if (content != null) { content.Apply(cfg); content.Resize(Bounds.Width, Bounds.Height); } }

        public void SetPaused(bool p)
        {
            if (p == IsPaused) return;
            IsPaused = p;
            if (content != null) { if (p) content.Pause(); else content.Play(); }
        }

        public void Destroy()
        {
            if (content != null) { if (root != null) content.DetachFrom(root); content.Dispose(); content = null; }
            DropHost();
            Current = null;
        }

        // ---------------- содержимое ----------------
        interface IContent : IDisposable
        {
            void AttachTo(ContainerVisual root);
            void DetachFrom(ContainerVisual root);
            void Resize(int w, int h);
            void Apply(Settings cfg);
            void Play();
            void Pause();
        }

        class ImageContent : IContent
        {
            protected readonly SpriteVisual sprite = Comp.C.CreateSpriteVisual();
            protected CompositionSurfaceBrush brush;
            public ImageContent(string file, Settings cfg)
            {
                if (file != null) { brush = Comp.FileBrush(file, 7680); sprite.Brush = brush; }
                sprite.RelativeSizeAdjustment = new Vector2(1, 1);
                Apply(cfg);
            }
            public void AttachTo(ContainerVisual root) { root.Children.InsertAtTop(sprite); }
            public void DetachFrom(ContainerVisual root) { try { root.Children.Remove(sprite); } catch { } }
            public void Resize(int w, int h) { }
            public virtual void Apply(Settings cfg) { if (brush != null) { brush.Stretch = Comp.StretchOf(cfg.Fit); brush.HorizontalAlignmentRatio = 0.5f; brush.VerticalAlignmentRatio = 0.5f; } }
            public virtual void Play() { }
            public virtual void Pause() { }
            public virtual void Dispose() { sprite.Dispose(); if (brush != null) brush.Dispose(); }
        }

        class VideoContent : ImageContent
        {
            readonly MediaPlayer player = new MediaPlayer();
            readonly MediaPlayerSurface surface;
            public VideoContent(string file, Settings cfg) : base(null, cfg)
            {
                player.Source = MediaSource.CreateFromUri(new Uri(file));
                player.IsLoopingEnabled = true;
                player.AutoPlay = false;
                player.CommandManager.IsEnabled = false; // не показываться в системной панели «Сейчас играет»
                surface = player.GetSurface(Comp.C);
                brush = Comp.C.CreateSurfaceBrush(surface.CompositionSurface);
                sprite.Brush = brush;
                Apply(cfg);
            }
            public override void Apply(Settings cfg)
            {
                base.Apply(cfg);
                if (player == null) return;
                player.IsMuted = cfg.Volume <= 0;
                player.Volume = Math.Max(0, Math.Min(100, cfg.Volume)) / 100.0;
                try { player.PlaybackSession.PlaybackRate = Math.Max(10, Math.Min(400, cfg.Speed)) / 100.0; } catch { }
            }
            public override void Play() { player.Play(); }
            public override void Pause() { player.Pause(); }
            public override void Dispose() { try { player.Pause(); } catch { } surface.Dispose(); player.Dispose(); base.Dispose(); }
        }

        class GifContent : ImageContent
        {
            readonly List<CompositionSurfaceBrush> frames = new List<CompositionSurfaceBrush>();
            readonly List<int> delays;
            readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render);
            int index;
            Settings cfg;
            public GifContent(string file, Settings cfg, string cache, int maxWidth) : base(null, cfg)
            {
                this.cfg = cfg;
                var anim = new GifAnimator(new Image(), file, maxWidth);
                delays = anim.Delays.ToList();
                foreach (var f in anim.Frames) frames.Add(Comp.Brush(f, cache));
                anim.Dispose();
                if (frames.Count > 0) { brush = frames[0]; sprite.Brush = brush; Apply(cfg); }
                timer.Tick += delegate
                {
                    if (frames.Count < 2) return;
                    index = (index + 1) % frames.Count;
                    brush = frames[index]; Apply(this.cfg); sprite.Brush = brush;
                    timer.Interval = TimeSpan.FromMilliseconds(delays[index]);
                };
            }
            public override void Apply(Settings c) { cfg = c; base.Apply(c); }
            public override void Play() { if (frames.Count > 1) { timer.Interval = TimeSpan.FromMilliseconds(delays[index]); timer.Start(); } }
            public override void Pause() { timer.Stop(); }
            public override void Dispose() { timer.Stop(); foreach (var f in frames) f.Dispose(); frames.Clear(); brush = null; base.Dispose(); }
        }

        // Сцена Wallpaper Engine: слои картинок, частицы и надписи внутри «холста» сцены
        class SceneContent : IContent
        {
            readonly SceneView model;
            readonly ContainerVisual stage = Comp.C.CreateContainerVisual();
            readonly List<CompParticles> particles = new List<CompParticles>();
            readonly List<Tuple<SceneView.TextLayer, SpriteVisual, int>> texts = new List<Tuple<SceneView.TextLayer, SpriteVisual, int>>();
            readonly DispatcherTimer textTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            readonly string cache;
            string fit;
            double scale = 1;
            int width, height;

            public SceneContent(string path, Settings cfg, string cache)
            {
                this.cache = cache; fit = cfg.Fit;
                model = new SceneView(path, WMedia.Stretch.Fill);
                if (model.Error != null) throw new InvalidDataException(model.Error);
                model.Measure(new System.Windows.Size(model.W, model.H));
                model.Arrange(new Rect(0, 0, model.W, model.H));
                model.UpdateLayout();
                model.UpdateLayout();

                var bg = model.Canvas.Background as WMedia.SolidColorBrush;
                if (bg != null)
                {
                    var back = Comp.C.CreateSpriteVisual();
                    back.Brush = Comp.C.CreateColorBrush(Windows.UI.Color.FromArgb(255, bg.Color.R, bg.Color.G, bg.Color.B));
                    back.Size = new Vector2((float)model.W, (float)model.H);
                    stage.Children.InsertAtTop(back);
                }
                foreach (UIElement el in model.Canvas.Children)
                {
                    var img = el as Image;
                    if (img != null && img.Source is BitmapSource)
                    {
                        var sv = Comp.C.CreateSpriteVisual();
                        sv.Brush = Comp.Brush((BitmapSource)img.Source, cache);
                        sv.Size = new Vector2((float)img.Width, (float)img.Height);
                        sv.Offset = new Vector3((float)Canvas.GetLeft(img), (float)Canvas.GetTop(img), 0);
                        sv.Opacity = (float)img.Opacity;
                        stage.Children.InsertAtTop(sv);
                        continue;
                    }
                    var ps = model.ParticleSystems.FirstOrDefault(p => p.Host == el);
                    if (ps != null)
                    {
                        var cp = new CompParticles(ps, cache);
                        stage.Children.InsertAtTop(cp.Layer);
                        particles.Add(cp);
                        continue;
                    }
                    var tl = model.TextLayers.FirstOrDefault(t => t.Host == el);
                    if (tl != null)
                    {
                        var sv = Comp.C.CreateSpriteVisual();
                        stage.Children.InsertAtTop(sv);
                        texts.Add(Tuple.Create(tl, sv, -1));
                    }
                }
                textTimer.Tick += delegate { model.RefreshTexts(); RenderTexts(false); };
            }

            void RenderTexts(bool force)
            {
                bool relayout = false;
                for (int i = 0; i < texts.Count; i++)
                    if (force || texts[i].Item1.Version != texts[i].Item3) relayout = true;
                if (!relayout) return;
                model.Measure(new System.Windows.Size(model.W, model.H)); model.Arrange(new Rect(0, 0, model.W, model.H));
                model.UpdateLayout(); model.UpdateLayout();
                for (int i = 0; i < texts.Count; i++)
                {
                    var t = texts[i];
                    if (!force && t.Item1.Version == t.Item3) continue;
                    var host = t.Item1.Host;
                    double w = host.ActualWidth, h = host.ActualHeight;
                    if (w < 1 || h < 1) continue;
                    double k = Math.Max(0.25, Math.Min(2, scale));
                    int pw = Math.Max(1, (int)Math.Ceiling(w * k)), ph = Math.Max(1, (int)Math.Ceiling(h * k));
                    // рисуем надпись из начала координат в нужном масштабе (через DPI), потом возвращаем на место
                    double left = Canvas.GetLeft(host), top = Canvas.GetTop(host);
                    Canvas.SetLeft(host, 0); Canvas.SetTop(host, 0);
                    model.UpdateLayout();
                    var rtb = new RenderTargetBitmap(pw, ph, 96 * k, 96 * k, WMedia.PixelFormats.Pbgra32);
                    rtb.Render(host);
                    Canvas.SetLeft(host, left); Canvas.SetTop(host, top);
                    model.UpdateLayout();
                    var old = t.Item2.Brush;
                    t.Item2.Brush = Comp.Brush(rtb, cache, true);
                    if (old != null) old.Dispose();
                    t.Item2.Size = new Vector2((float)w, (float)h);
                    t.Item2.Offset = new Vector3((float)left, (float)top, 0);
                    texts[i] = Tuple.Create(t.Item1, t.Item2, t.Item1.Version);
                }
            }

            public void AttachTo(ContainerVisual root) { root.Children.InsertAtTop(stage); }
            public void DetachFrom(ContainerVisual root) { try { root.Children.Remove(stage); } catch { } }

            public void Resize(int w, int h)
            {
                width = w; height = h;
                double sx = w / model.W, sy = h / model.H;
                double s = fit == "fit" ? Math.Min(sx, sy) : Math.Max(sx, sy);
                if (fit == "stretch") { stage.Scale = new Vector3((float)sx, (float)sy, 1); stage.Offset = Vector3.Zero; scale = Math.Max(sx, sy); }
                else
                {
                    stage.Scale = new Vector3((float)s, (float)s, 1);
                    stage.Offset = new Vector3((float)((w - model.W * s) / 2), (float)((h - model.H * s) / 2), 0);
                    scale = s;
                }
                RenderTexts(true);
            }

            public void Apply(Settings cfg) { fit = cfg.Fit; }

            public void Play()
            {
                foreach (var p in particles) p.Play();
                model.RefreshTexts(); RenderTexts(false);
                textTimer.Start();
            }

            public void Pause()
            {
                foreach (var p in particles) p.Pause();
                textTimer.Stop();
            }

            public void Dispose()
            {
                textTimer.Stop();
                foreach (var p in particles) p.Dispose();
                foreach (var t in texts) { if (t.Item2.Brush != null) t.Item2.Brush.Dispose(); t.Item2.Dispose(); }
                stage.Dispose();
                model.Dispose();
            }
        }

        // Частицы: путь каждой частицы просчитывается один раз при рождении, дальше её ведёт композитор
        class CompParticles : IDisposable
        {
            public readonly ContainerVisual Layer = Comp.C.CreateContainerVisual();
            readonly ParticleSystem ps;
            readonly CompositionSurfaceBrush[][] brushes;
            readonly List<Tuple<SpriteVisual, DateTime>> live = new List<Tuple<SpriteVisual, DateTime>>();
            readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            readonly CompositionEasingFunction linear = Comp.C.CreateLinearEasingFunction();
            double acc, clock;
            DateTime last;
            bool warmed;

            public CompParticles(ParticleSystem ps, string cache)
            {
                this.ps = ps;
                brushes = ps.Frames.Select(set => set.Select(f => Comp.Brush(f, cache)).ToArray()).ToArray();
                timer.Tick += Tick;
            }

            public void Play()
            {
                last = DateTime.UtcNow;
                if (!warmed)
                {
                    warmed = true;
                    // как будто система уже работала StartTime секунд: рождаем частицы «посреди жизни»
                    double pre = Math.Min(ps.StartTime, 30);
                    int n = (int)Math.Min(ps.MaxCount, ps.Rate * pre);
                    var r = new Random();
                    for (int i = 0; i < n; i++) Spawn(r.NextDouble() * pre);
                }
                timer.Start();
            }

            public void Pause() { timer.Stop(); } // живущие частицы спокойно догорают, новых нет

            void Tick(object s, EventArgs e)
            {
                var now = DateTime.UtcNow;
                double dt = Math.Min(0.5, (now - last).TotalSeconds);
                last = now; clock += dt;
                for (int i = live.Count - 1; i >= 0; i--)
                    if (live[i].Item2 <= now) { Layer.Children.Remove(live[i].Item1); live[i].Item1.Dispose(); live.RemoveAt(i); }
                acc += ps.Rate * dt;
                while (acc >= 1) { acc -= 1; if (live.Count < ps.MaxCount) Spawn(0); }
            }

            void Spawn(double age)
            {
                var p = ps.Create();
                if (age >= p.Life) return;
                const double step = 1 / 30.0;
                double t = clock;
                // доходим до нужного возраста
                while (p.Age < age) { ps.Advance(p, step, t); t += step; }
                double startAge = p.Age, rest = p.Life - startAge;
                if (rest <= 0.05) return;
                double H = ps.Height;
                double size = p.Size * ps.SizeAt(startAge / p.Life);
                var sv = Comp.C.CreateSpriteVisual();
                var set = brushes[p.Tint];
                sv.Brush = set[ps.Sequence ? 0 : p.Frame];
                sv.Size = new Vector2((float)size, (float)size);
                sv.CenterPoint = new Vector3((float)size / 2, (float)size / 2, 0);

                var off = Comp.C.CreateVector3KeyFrameAnimation();
                var rot = Comp.C.CreateScalarKeyFrameAnimation();
                var op = Comp.C.CreateScalarKeyFrameAnimation();
                int samples = 12;
                double sampleDt = rest / samples, next = 0;
                for (int i = 0; i <= samples; i++)
                {
                    float f = (float)i / samples;
                    var pos = new Vector3((float)(p.X - size / 2), (float)(H - p.Y - size / 2), 0);
                    if (i == 0) sv.Offset = pos;
                    off.InsertKeyFrame(f, pos, linear);
                    rot.InsertKeyFrame(f, (float)(-p.Rot * 180 / Math.PI), linear);
                    op.InsertKeyFrame(f, (float)ps.FadeAt(p.Age / p.Life, p.Alpha), linear);
                    next += sampleDt;
                    while (p.Age - startAge < next - 1e-9 && i < samples) { ps.Advance(p, Math.Min(step, next - (p.Age - startAge)), t); t += step; }
                }
                var dur = TimeSpan.FromSeconds(rest);
                off.Duration = dur; rot.Duration = dur; op.Duration = dur;
                sv.Opacity = 0;
                sv.StartAnimation("Offset", off);
                sv.StartAnimation("RotationAngleInDegrees", rot);
                sv.StartAnimation("Opacity", op);
                Layer.Children.InsertAtTop(sv);
                live.Add(Tuple.Create(sv, DateTime.UtcNow + dur));
                off.Dispose(); rot.Dispose(); op.Dispose();
            }

            public void Dispose()
            {
                timer.Stop();
                foreach (var l in live) l.Item1.Dispose();
                live.Clear();
                foreach (var set in brushes) foreach (var b in set) b.Dispose();
                Layer.Dispose();
            }
        }
    }
}

