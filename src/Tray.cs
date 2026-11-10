using System;
using System.Drawing;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace Vivid
{
    class Tray : IDisposable
    {
        readonly Core core;
        readonly WinForms.NotifyIcon icon;
        readonly WinForms.ToolStripMenuItem pauseItem, offItem;

        public Tray(Core core)
        {
            this.core = core;
            icon = new WinForms.NotifyIcon();
            try { icon.Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { icon.Icon = SystemIcons.Application; }
            icon.Text = "Vivid — живые обои";

            var menu = new WinForms.ContextMenuStrip();
            menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new DarkColors());
            menu.ForeColor = Color.FromArgb(230, 232, 240);
            menu.ShowImageMargin = false;
            menu.Items.Add("Открыть", null, delegate { core.ShowUi(); });
            pauseItem = new WinForms.ToolStripMenuItem("Пауза", null, delegate { core.TogglePause(); });
            menu.Items.Add(pauseItem);
            menu.Items.Add("Случайные обои", null, delegate
            {
                var list = core.Lib.Items.Where(i => i.Exists).ToList();
                if (list.Count == 0) return;
                var cur = core.Cfg.Current;
                var pick = list.Count > 1 ? list.Where(i => i.Id != cur).OrderBy(i => Guid.NewGuid()).First() : list[0];
                core.Apply(pick, null);
            });
            offItem = new WinForms.ToolStripMenuItem("Выключить живые обои", null, delegate { core.TurnOff(); });
            menu.Items.Add(offItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { core.Exit(); });
            icon.ContextMenuStrip = menu;
            icon.MouseClick += delegate(object s, WinForms.MouseEventArgs e) { if (e.Button == WinForms.MouseButtons.Left) core.ShowUi(); };
            icon.Visible = true;
        }

        public void Refresh()
        {
            pauseItem.Text = core.Cfg.Paused ? "Продолжить" : "Пауза";
            pauseItem.Enabled = core.Active;
            offItem.Enabled = core.Active;
        }

        // после перезапуска проводника значок нужно показать заново
        public void Readd() { icon.Visible = false; icon.Visible = true; }

        public void Dispose() { icon.Visible = false; icon.Dispose(); }

        class DarkColors : WinForms.ProfessionalColorTable
        {
            static readonly Color bg = Color.FromArgb(24, 26, 33), hi = Color.FromArgb(44, 40, 70), line = Color.FromArgb(48, 52, 64);
            public override Color ToolStripDropDownBackground { get { return bg; } }
            public override Color MenuBorder { get { return line; } }
            public override Color MenuItemBorder { get { return hi; } }
            public override Color MenuItemSelected { get { return hi; } }
            public override Color MenuItemSelectedGradientBegin { get { return hi; } }
            public override Color MenuItemSelectedGradientEnd { get { return hi; } }
            public override Color SeparatorDark { get { return line; } }
            public override Color SeparatorLight { get { return bg; } }
            public override Color ImageMarginGradientBegin { get { return bg; } }
            public override Color ImageMarginGradientMiddle { get { return bg; } }
            public override Color ImageMarginGradientEnd { get { return bg; } }
        }
    }
}
