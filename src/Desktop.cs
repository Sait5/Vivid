using System;

namespace Vivid
{
    // Встраивание окна обоев за иконки рабочего стола.
    // Два режима Windows:
    //  - классический (до Windows 11 24H2): отдельное верхнее окно WorkerW позади иконок — становимся его дочерним;
    //  - новый (24H2 и новее, Progman с WS_EX_NOREDIRECTIONBITMAP): иконки (SHELLDLL_DefView) и WorkerW — дети Progman;
    //    становимся ребёнком Progman между ними и включаем WS_EX_LAYERED, иначе композитор окно не покажет.
    static class DesktopHost
    {
        public static IntPtr Progman, DefView, Host;
        public static bool Raised;

        public static bool Find()
        {
            Progman = Native.FindWindow("Progman", null);
            DefView = IntPtr.Zero; Host = IntPtr.Zero;
            if (Progman == IntPtr.Zero) return false;

            IntPtr r;
            // Просим проводник создать слой WorkerW позади иконок
            Native.SendMessageTimeout(Progman, 0x052C, new IntPtr(0xD), new IntPtr(1), Native.SMTO_NORMAL, 1000, out r);
            Native.SendMessageTimeout(Progman, 0x052C, IntPtr.Zero, IntPtr.Zero, Native.SMTO_NORMAL, 1000, out r);

            Raised = (Native.GetWindowLong(Progman, Native.GWL_EXSTYLE) & Native.WS_EX_NOREDIRECTIONBITMAP) != 0;
            if (Raised)
            {
                DefView = Native.FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                // Новый рабочий стол (Windows 11 24H2+): обычную картинку рисует WorkerW внутри Progman, под иконками.
                // Наше окно становится его дочерним, а содержимое рисует системный композитор — только такое здесь видно.
                IntPtr inner = Native.FindWindowEx(Progman, IntPtr.Zero, "WorkerW", null);
                Host = inner != IntPtr.Zero ? inner : Progman;
                return DefView != IntPtr.Zero;
            }

            IntPtr found = IntPtr.Zero, dv = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                IntPtr d = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (d != IntPtr.Zero)
                {
                    dv = d;
                    found = Native.FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                }
                return true;
            }, IntPtr.Zero);
            DefView = dv;
            Host = found;
            if (Host == IntPtr.Zero && dv != IntPtr.Zero && Native.GetParent(dv) == Progman)
                Host = Progman; // иконки живут в Progman, а WorkerW так и не появился
            return Host != IntPtr.Zero;
        }

        public static bool Valid
        {
            get { return Host != IntPtr.Zero && Native.IsWindow(Host) && Native.IsWindow(Progman); }
        }

        public static bool Attach(IntPtr hwnd, System.Drawing.Rectangle screen)
        {
            if (!Valid && !Find()) return false;

            int style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
            style &= ~(Native.WS_POPUP | Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
            style |= Native.WS_CHILD | Native.WS_CLIPSIBLINGS;
            Native.SetWindowLong(hwnd, Native.GWL_STYLE, style);

            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            ex &= ~Native.WS_EX_APPWINDOW;
            ex |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            if (Raised) ex |= Native.WS_EX_LAYERED;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);
            if (Raised) Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);

            Native.SetParent(hwnd, Host);
            Place(hwnd, screen);
            return true;
        }

        // Окно уже создано дочерним — включаем «слоистость» для нового рабочего стола
        public static void Prepare(IntPtr hwnd)
        {
            if (!Raised || Host != Progman) return;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
            Native.SetLayeredWindowAttributes(hwnd, 0, 255, Native.LWA_ALPHA);
        }

        public static void Place(IntPtr hwnd, System.Drawing.Rectangle screen)
        {
            var rc = new Native.RECT { Left = screen.Left, Top = screen.Top, Right = screen.Right, Bottom = screen.Bottom };
            Native.MapWindowPoints(IntPtr.Zero, Host, ref rc, 2); // экранные координаты → координаты внутри родителя
            // В новом режиме ставим окно сразу под иконками (выше штатного WorkerW с картинкой)
            IntPtr after = Raised && Host == Progman && DefView != IntPtr.Zero ? DefView : IntPtr.Zero;
            uint flags = Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW;
            if (after == IntPtr.Zero) flags |= Native.SWP_NOZORDER;
            Native.SetWindowPos(hwnd, after, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, flags);
        }

        public static bool IsAttached(IntPtr hwnd)
        {
            return Valid && Native.GetParent(hwnd) == Host;
        }
    }
}
