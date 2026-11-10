using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Backdrop
{
    static class Program
    {
        public const string Version = "1.0";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "/render")
            {
                // служебное: отрисовать сцену в PNG (проверка без рабочего стола)
                var bmp = Thumbs.RenderScene(args[1], 1920, 1080);
                if (bmp == null) return 1;
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = System.IO.File.Create(args[2])) enc.Save(fs);
                return 0;
            }
            bool quit = args.Any(a => a.Equals("/quit", StringComparison.OrdinalIgnoreCase));
            bool tray = args.Any(a => a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

            var files = args.Where(a => !a.StartsWith("/") && (System.IO.File.Exists(a) || System.IO.Directory.Exists(a))).Select(System.IO.Path.GetFullPath).ToArray();

            bool created;
            var mutex = new Mutex(true, "Backdrop.Wallpaper.Instance", out created);
            if (!created)
            {
                // уже запущена — передаём ей файлы или просим показать окно (или закрыться)
                if (files.Length > 0 && !quit) Native.SendFiles(files);
                else Native.PostMessage(Native.HWND_BROADCAST, quit ? Msg.Quit : Msg.Show, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            if (quit) return 0;

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("FATAL " + e.ExceptionObject); };
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Log.Write(e.Exception);
                e.Handled = true;
            };
            var core = new Core();
            app.Startup += delegate { core.Start(!tray); if (files.Length > 0) core.OpenFiles(files); };
            app.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
    }
}
