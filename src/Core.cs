using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace Backdrop
{
    static class Msg
    {
        public static readonly uint Show = Native.RegisterWindowMessage("Backdrop.Wallpaper.Show");
        public static readonly uint Quit = Native.RegisterWindowMessage("Backdrop.Wallpaper.Quit");
        public static readonly uint TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    }

    // Сердце программы: окна обоев на мониторах, авто-пауза, восстановление после перезапуска проводника.
    class Core
    {
        public static Core I;
        public Settings Cfg;
        public Library Lib;
        public event Action Changed;

        readonly List<WallpaperWindow> walls = new List<WallpaperWindow>();
        readonly HashSet<IntPtr> own = new HashSet<IntPtr>();
        HwndSource msgWindow;
        DispatcherTimer timer, rebuildTimer;
        Tray tray;
        MainWindow ui;
        public string Status = "";
        int failedAttach;

        public void Start(bool showUi)
        {
            I = this;
            Cfg = Settings.Load();
            Lib = Library.Load();
            if (string.IsNullOrEmpty(Cfg.Accent) || !Theme.SetAccent(Cfg.Accent)) Cfg.Accent = Theme.AccentHex;

            var p = new HwndSourceParameters("Backdrop.Messages") { WindowStyle = Native.WS_POPUP, Width = 0, Height = 0 };
            msgWindow = new HwndSource(p);
            msgWindow.AddHook(Hook);

            tray = new Tray(this);
            SystemEvents.DisplaySettingsChanged += delegate { ScheduleRebuild(1500); };
            SystemEvents.SessionSwitch += delegate(object s, SessionSwitchEventArgs e)
            {
                if (e.Reason == SessionSwitchReason.SessionUnlock) ScheduleRebuild(800);
            };

            rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            rebuildTimer.Tick += delegate { rebuildTimer.Stop(); Rebuild(); };

            Rebuild();
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += Tick;
            timer.Start();

            // Запуск вместе с Windows (/tray): окно не показываем — только значок в трее, если не включено в настройках.
            // Ручной запуск (ярлык) — окно открывается всегда.
            if (showUi || Cfg.ShowOnStartup) ShowUi();
        }

        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            uint m = (uint)msg;
            if (m == Msg.Show) { ShowUi(); handled = true; }
            else if (m == Msg.Quit) { Exit(); handled = true; }
            else if (m == Msg.TaskbarCreated) { ScheduleRebuild(1500); tray.Readd(); }
            else if (msg == Native.WM_COPYDATA)
            {
                try
                {
                    var cds = (Native.COPYDATASTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(l, typeof(Native.COPYDATASTRUCT));
                    if (cds.dwData == new IntPtr(0x42445250) && cds.cbData > 0)
                    {
                        var bytes = new byte[cds.cbData];
                        System.Runtime.InteropServices.Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
                        var files = System.Text.Encoding.UTF8.GetString(bytes).Split('\n');
                        Application.Current.Dispatcher.BeginInvoke(new Action(delegate { OpenFiles(files); }));
                    }
                }
                catch (Exception e) { Log.Write(e); }
                handled = true;
                return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        // Файлы, переданные при запуске («Открыть с помощью» / перетаскивание на exe): добавить и сразу поставить первый
        public void OpenFiles(string[] files)
        {
            int before = Lib.Items.Count;
            var r = Import.FromFiles(Lib, files);
            Lib.Save();
            Item first = null;
            foreach (var f in files)
            {
                first = Lib.Items.FirstOrDefault(i => string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase));
                if (first != null) break;
            }
            if (first == null && Lib.Items.Count > before) first = Lib.Items[Lib.Items.Count - 1];
            if (first != null) Apply(first, null);
            if (ui != null) { ui.Close(); ShowUi(); }
        }

        public void ScheduleRebuild(int ms)
        {
            rebuildTimer.Stop();
            rebuildTimer.Interval = TimeSpan.FromMilliseconds(ms);
            rebuildTimer.Start();
        }

        public bool Active { get { return walls.Any(w => w.Current != null); } }

        // Пересоздать окна под текущие мониторы
        public void Rebuild()
        {
            var screens = WinForms.Screen.AllScreens;
            bool anyItem = screens.Any(s => Lib.Get(Cfg.For(s.DeviceName)) != null);

            // убрать окна мониторов, которых больше нет или которым обои не нужны
            foreach (var w in walls.ToList())
            {
                var scr = screens.FirstOrDefault(s => s.DeviceName == w.Monitor);
                if (scr == null || Lib.Get(Cfg.For(w.Monitor)) == null) { Remove(w); }
            }
            if (!anyItem) { Notify(); return; }

            if (!DesktopHost.Find())
            {
                Status = "Не найден рабочий стол проводника — повторю попытку";
                ScheduleRebuild(3000);
                Notify(); return;
            }

            foreach (var s in screens)
            {
                var item = Lib.Get(Cfg.For(s.DeviceName));
                if (item == null) continue;
                var w = walls.FirstOrDefault(x => x.Monitor == s.DeviceName);
                if (w == null)
                {
                    w = new WallpaperWindow(s.DeviceName, s.Bounds);
                    own.Add(w.Hwnd);
                    walls.Add(w);
                    w.Attach();
                    w.Load(item, Cfg);
                }
                else
                {
                    w.Bounds = s.Bounds;
                    if (!w.IsAttached) w.Attach(); else w.Move(s.Bounds);
                    if (w.Current == null || w.Current.Id != item.Id) w.Load(item, Cfg);
                }
            }
            Status = "";
            Tick(null, null);
            Notify();
        }

        void Remove(WallpaperWindow w)
        {
            walls.Remove(w); own.Remove(w.Hwnd);
            w.Destroy();
            Native.RefreshStaticWallpaper();
        }

        void Notify() { var c = Changed; if (c != null) c(); tray.Refresh(); }

        // ---------- команды ----------
        public void Apply(Item item, string monitor)
        {
            if (item == null) return;
            if (Cfg.SameOnAll || monitor == null) { Cfg.Current = item.Id; if (Cfg.PerMonitor != null) Cfg.PerMonitor.Clear(); }
            else Cfg.PerMonitor[monitor] = item.Id;
            if (Cfg.Current == null) Cfg.Current = item.Id;
            Cfg.Paused = false;
            Cfg.Save();
            Rebuild();
        }

        public void TurnOff()
        {
            Cfg.Current = null;
            if (Cfg.PerMonitor != null) Cfg.PerMonitor.Clear();
            Cfg.Save();
            foreach (var w in walls.ToList()) Remove(w);
            Notify();
        }

        public void Remove(Item item)
        {
            Lib.Items.Remove(item);
            Lib.Save();
            if (Cfg.Current == item.Id) Cfg.Current = null;
            foreach (var k in Cfg.PerMonitor.Where(kv => kv.Value == item.Id).Select(kv => kv.Key).ToList()) Cfg.PerMonitor.Remove(k);
            Cfg.Save();
            Rebuild();
        }

        public void TogglePause() { Cfg.Paused = !Cfg.Paused; Cfg.Save(); Tick(null, null); Notify(); }

        public void SettingsChanged()
        {
            Cfg.Save();
            foreach (var w in walls) { w.ApplyAudio(Cfg); w.ApplyFit(Cfg); w.ApplySpeed(Cfg); }
            Tick(null, null);
            Notify();
        }

        public Item CurrentFor(string monitor) { return Lib.Get(Cfg.For(monitor)); }

        public string PauseReason(string monitor)
        {
            var w = walls.FirstOrDefault(x => x.Monitor == monitor);
            return w == null ? null : (w.IsPaused ? (Cfg.Paused ? "на паузе" : "пауза: окно поверх") : "играет");
        }

        // Сменили цвет — пересобираем окно (стили строятся из акцента), настройки остаются открытыми
        public void ReopenUi()
        {
            if (ui == null) return;
            var old = ui;
            var state = old.WindowState; double l = old.Left, t = old.Top, w = old.Width, h = old.Height;
            ui = null;
            ShowUi();
            ui.WindowStartupLocation = WindowStartupLocation.Manual;
            ui.Left = l; ui.Top = t; ui.Width = w; ui.Height = h; ui.WindowState = state;
            ui.OpenSettings();
            old.Close();
        }

        public void ShowUi()
        {
            if (ui == null)
            {
                var win = new MainWindow(this);
                ui = win;
                win.Closed += delegate { if (ui == win) ui = null; System.GC.Collect(); };
                ui.Show();
            }
            if (ui.WindowState == WindowState.Minimized) ui.WindowState = WindowState.Normal;
            ui.Activate();
        }

        public void Exit()
        {
            timer.Stop();
            foreach (var w in walls.ToList()) { walls.Remove(w); w.Destroy(); }
            Native.RefreshStaticWallpaper();
            tray.Dispose();
            if (ui != null) ui.Close();
            Application.Current.Shutdown();
        }

        // ---------- авто-пауза ----------
        void Tick(object s, EventArgs e)
        {
            if (walls.Count == 0) return;

            // проводник перезапустился или окно отвалилось — встраиваем заново
            if (walls.Any(w => !w.IsAttached))
            {
                if (++failedAttach % 3 == 1)
                {
                    DesktopHost.Find();
                    foreach (var w in walls) if (!w.IsAttached) w.Attach();
                }
            }
            else failedAttach = 0;

            var covered = new HashSet<string>();
            if (!Cfg.Paused && (Cfg.PauseFullscreen || Cfg.PauseMaximized || Cfg.PauseFocused))
                FindCovered(covered);

            foreach (var w in walls) w.SetPaused(Cfg.Paused || covered.Contains(w.Monitor));
        }

        static readonly HashSet<string> shellClasses = new HashSet<string>
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SHELLDLL_DefView",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
            "NotifyIconOverflowWindow", "Xaml_WindowedPopupClass", "ForegroundStaging", "MultitaskingViewFrame",
            "TaskListThumbnailWnd", "#32768"
        };

        void FindCovered(HashSet<string> covered)
        {
            IntPtr fg = Native.GetForegroundWindow();
            IntPtr shell = Native.GetShellWindow();

            // 1) Активное окно: на весь экран (игры, видео) или просто любое активное — по настройке
            if (fg != IntPtr.Zero && fg != shell && !own.Contains(fg) && IsRealWindow(fg))
            {
                string mon = Native.MonitorName(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONULL));
                if (mon != null)
                {
                    if (Cfg.PauseFocused) covered.Add(mon);
                    else if (Cfg.PauseFullscreen && CoversMonitor(fg)) covered.Add(mon);
                }
            }

            // 2) Развёрнутые окна на мониторе (видны поверх обоев целиком)
            if (Cfg.PauseMaximized || Cfg.PauseFullscreen)
            {
                Native.EnumWindows(delegate(IntPtr h, IntPtr l)
                {
                    if (h == shell || own.Contains(h) || !IsRealWindow(h)) return true;
                    bool zoomed = Cfg.PauseMaximized && Native.IsZoomed(h);
                    bool full = Cfg.PauseFullscreen && CoversMonitor(h);
                    if (zoomed || full)
                    {
                        string mon = Native.MonitorName(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONULL));
                        if (mon != null) covered.Add(mon);
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }

        static bool IsRealWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsCloaked(h)) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) return false;               // прозрачные оверлеи (FPS-счётчики и т.п.)
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return false;
            if ((ex & Native.WS_EX_LAYERED) != 0 && (ex & Native.WS_EX_NOACTIVATE) != 0) return false;
            return !shellClasses.Contains(Native.ClassOf(h));
        }

        static bool CoversMonitor(IntPtr h)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return false;
            IntPtr hm = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONULL);
            if (hm == IntPtr.Zero) return false;
            var mi = new Native.MONITORINFOEX();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
            if (!Native.GetMonitorInfo(hm, ref mi)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }

        // ---------- автозапуск ----------
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Autostart
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("Backdrop") != null; }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue("Backdrop", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" /tray");
                        else k.DeleteValue("Backdrop", false);
                    }
                }
                catch (Exception e) { Log.Write(e); }
            }
        }
    }
}
