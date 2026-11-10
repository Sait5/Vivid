using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Backdrop
{
    // Окно-«кинотеатр»: выбранные обои живут на весь фон окна, поверх — название, кнопки и лента коллекции.
    class MainWindow : Window
    {
        readonly Core core;
        Grid rootGrid, stage, previewHost;
        FrameworkElement infoBlock, emptyBlock, strip;
        Border drawer;
        TranslateTransform drawerShift;
        StackPanel cardRow;
        ScrollViewer stripScroll;
        TextBox search;
        TextBlock searchHint, countText;
        string filterKind = "all";
        readonly Dictionary<string, Border> cards = new Dictionary<string, Border>();

        MediaElement previewVideo;
        GifAnimator previewGif;
        SceneView previewScene;
        TextBlock selTitle, selMeta;
        StackPanel monitorRow;
        Button applyBtn;
        Item selected;
        string selectedMonitor;

        Ellipse statusDot;
        TextBlock statusText, statsText;
        Border toast;
        TextBlock toastText;
        DispatcherTimer statsTimer, toastTimer;
        TimeSpan lastCpu; DateTime lastWall;

        public MainWindow(Core core)
        {
            this.core = core;
            Resources.MergedDictionaries.Add(Theme.Build());
            Title = "Backdrop — живые обои";
            Width = 1280; Height = 800; MinWidth = 1000; MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.Bg; Foreground = Theme.Text;
            FontFamily = Theme.Font; FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            try
            {
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
                    Icon = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 64, ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
            });
            SourceInitialized += delegate { Native.DarkFrame(new WindowInteropHelper(this).Handle); };
            StateChanged += delegate { rootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); };

            rootGrid = new Grid { ClipToBounds = true };
            Content = rootGrid;

            // 1) сцена: живые обои на весь фон
            stage = new Grid { Background = Theme.Bg };
            previewHost = new Grid();
            RenderOptions.SetBitmapScalingMode(previewHost, BitmapScalingMode.HighQuality);
            stage.Children.Add(previewHost);
            stage.Children.Add(Shade(new Point(0, 0), new Point(0, 1), new[] { 0.0, 0.16 }, new byte[] { 0xA0, 0x00 }));          // верх — под панель
            stage.Children.Add(Shade(new Point(0, 0), new Point(0, 1), new[] { 0.42, 0.72, 1.0 }, new byte[] { 0x00, 0xB0, 0xF4 })); // низ — под ленту
            stage.Children.Add(Shade(new Point(0, 0), new Point(1, 0), new[] { 0.0, 0.55 }, new byte[] { 0x90, 0x00 }));          // слева — под заголовок
            rootGrid.Children.Add(stage);

            // 2) интерфейс поверх
            var ui = new Grid();
            ui.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            ui.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ui.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.Children.Add(ui);
            ui.Children.Add(BuildTopBar());

            infoBlock = BuildInfo();
            Grid.SetRow(infoBlock, 1);
            ui.Children.Add(infoBlock);
            emptyBlock = BuildEmpty();
            Grid.SetRow(emptyBlock, 1);
            ui.Children.Add(emptyBlock);

            strip = BuildStrip();
            Grid.SetRow(strip, 2);
            ui.Children.Add(strip);

            // 3) выезжающие настройки
            var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), Visibility = Visibility.Collapsed, Name = "dim" };
            dim.MouseLeftButtonDown += delegate { ToggleDrawer(false); };
            rootGrid.Children.Add(dim);
            drawer = BuildDrawer();
            rootGrid.Children.Add(drawer);

            // 4) всплывающие сообщения
            toast = new Border
            {
                Background = Theme.GlassStrong, BorderBrush = Theme.GlassLine, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22), Padding = new Thickness(20, 12, 22, 12), Margin = new Thickness(0, 76, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false, MaxWidth = 760
            };
            var trow = new StackPanel { Orientation = Orientation.Horizontal };
            trow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            toastText = new TextBlock { Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 700 };
            trow.Children.Add(toastText);
            toast.Child = trow;
            rootGrid.Children.Add(toast);
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            toastTimer.Tick += delegate { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };

            AllowDrop = true;
            DragOver += delegate(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += OnDrop;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) ToggleDrawer(false); };

            core.Changed += OnCoreChanged;
            statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            statsTimer.Tick += delegate { UpdateStats(); RefreshStatus(); };
            statsTimer.Start();
            lastCpu = Process.GetCurrentProcess().TotalProcessorTime; lastWall = DateTime.UtcNow;

            FillStrip();
            var cur = core.CurrentFor(null);
            Select(cur ?? core.Lib.Items.OrderByDescending(i => i.Added).FirstOrDefault());
            RefreshStatus();
            if (!string.IsNullOrEmpty(core.Status)) ShowToast(core.Status);
        }

        static Border Shade(Point a, Point b, double[] stops, byte[] alpha)
        {
            var g = new LinearGradientBrush { StartPoint = a, EndPoint = b };
            for (int i = 0; i < stops.Length; i++) g.GradientStops.Add(new GradientStop(Color.FromArgb(alpha[i], 7, 7, 9), stops[i]));
            g.Freeze();
            return new Border { Background = g, IsHitTestVisible = false };
        }

        protected override void OnClosed(EventArgs e)
        {
            core.Changed -= OnCoreChanged;
            statsTimer.Stop(); toastTimer.Stop();
            StopPreview();
            base.OnClosed(e);
        }

        void OnCoreChanged() { RefreshStatus(); RefreshCards(); }

        static TextBlock Kicker(string text, Brush color)
        {
            return new TextBlock { Text = Theme.Spaced(text), FontFamily = Theme.Head, FontSize = 11, Foreground = color };
        }

        static Border Glass(UIElement child, double radius, Thickness pad)
        {
            return new Border { Background = Theme.Glass, BorderBrush = Theme.GlassLine, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = pad, Child = child };
        }

        // ================= верх =================
        FrameworkElement BuildTopBar()
        {
            var g = new Grid { Margin = new Thickness(22, 0, 8, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            logo.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 12, 0) });
            logo.Children.Add(new TextBlock { Text = "Backdrop", FontFamily = Theme.Head, FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(logo);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 2);

            // спокойная строка статуса — просто текст, без плашек
            var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
            statusDot = new Ellipse { Width = 6, Height = 6, Fill = Theme.Dim, VerticalAlignment = VerticalAlignment.Center };
            statusText = new TextBlock { Margin = new Thickness(8, 0, 0, 0), FontFamily = Theme.Head, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            statsText = new TextBlock { Margin = new Thickness(16, 0, 0, 0), FontFamily = Theme.Head, FontSize = 11, Foreground = Theme.Faint, VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(statusDot); st.Children.Add(statusText); st.Children.Add(statsText);
            st.ToolTip = "Сколько сейчас тратит сама программа";
            right.Children.Add(st);

            var gear = CaptionButton("", "Настройки", delegate { ToggleDrawer(true); });
            ((TextBlock)gear.Content).FontSize = 13;
            right.Children.Add(gear);
            right.Children.Add(new Border { Width = 1, Height = 18, Background = Theme.Line, Margin = new Thickness(8, 0, 8, 0) });
            right.Children.Add(CaptionButton("", "Свернуть", delegate { WindowState = WindowState.Minimized; }));
            right.Children.Add(CaptionButton("", "Развернуть", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            right.Children.Add(CaptionButton("", "Закрыть окно — обои продолжат работать, программа уйдёт в трей", delegate { Close(); }));
            g.Children.Add(right);
            return g;
        }

        Button RoundIcon(string glyph, string tip, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 14 },
                Style = (Style)Resources["RoundBtn"], Width = 40, Height = 40, Padding = new Thickness(0), ToolTip = tip,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            b.Click += click;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        Button CaptionButton(string glyph, string tip, RoutedEventHandler click)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 10 },
                Style = (Style)Resources["GhostBtn"], Width = 40, Height = 36, Padding = new Thickness(0), ToolTip = tip,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            b.Click += click;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        void RefreshStatus()
        {
            if (!core.Active) { statusDot.Fill = Theme.Faint; statusText.Text = Theme.Spaced("ВЫКЛЮЧЕНО"); statusText.Foreground = Theme.Dim; return; }
            var mons = WinForms.Screen.AllScreens.Select(s => core.PauseReason(s.DeviceName)).Where(x => x != null).ToList();
            statusText.Foreground = Theme.Text;
            if (core.Cfg.Paused) { statusDot.Fill = Theme.Warn; statusText.Text = Theme.Spaced("ПАУЗА"); }
            else if (mons.Contains("играет")) { statusDot.Fill = Theme.Good; statusText.Text = Theme.Spaced("НА СТОЛЕ"); }
            else { statusDot.Fill = Theme.Warn; statusText.Text = Theme.Spaced("ЖДЁТ — ОКНО ПОВЕРХ"); }
        }

        void UpdateStats()
        {
            try
            {
                var p = Process.GetCurrentProcess();
                var cpu = p.TotalProcessorTime; var now = DateTime.UtcNow;
                double pct = (cpu - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastWall).TotalMilliseconds) / Environment.ProcessorCount * 100;
                lastCpu = cpu; lastWall = now;
                statsText.Text = "ЦП " + pct.ToString("0.0") + "%   " + (p.WorkingSet64 / (1024 * 1024)) + " МБ";
            }
            catch { }
        }

        public void ShowToast(string text)
        {
            toastText.Text = text;
            toast.Visibility = Visibility.Visible;
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            toastTimer.Stop(); toastTimer.Interval = TimeSpan.FromSeconds(Math.Max(4, text.Length / 14.0)); toastTimer.Start();
        }

        // ================= заголовок выбранных обоев =================
        FrameworkElement BuildInfo()
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(48, 0, 40, 26), MaxWidth = 860 };
            var kick = new StackPanel { Orientation = Orientation.Horizontal };
            kick.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            selMeta = new TextBlock { FontFamily = Theme.Head, FontSize = 11.5, Foreground = Theme.Dim, VerticalAlignment = VerticalAlignment.Center };
            kick.Children.Add(selMeta);
            sp.Children.Add(kick);
            selTitle = new TextBlock
            {
                FontFamily = Theme.Head, FontWeight = FontWeights.SemiBold, FontSize = 58, TextWrapping = TextWrapping.Wrap,
                LineHeight = 60, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 124, TextTrimming = TextTrimming.WordEllipsis,
                Margin = new Thickness(0, 14, 0, 22)
            };
            sp.Children.Add(selTitle);
            monitorRow = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            sp.Children.Add(monitorRow);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            applyBtn = new Button { Style = (Style)Resources["AccentBtn"], Padding = new Thickness(26, 14, 26, 14), FontSize = 13 };
            applyBtn.Click += delegate { ApplySelected(); };
            row.Children.Add(applyBtn);
            row.Children.Add(new Border { Width = 10 });
            row.Children.Add(IconButton("", "ФАЙЛ", "Btn", delegate
            {
                if (selected == null) return;
                try { Process.Start("explorer.exe", "/select,\"" + selected.Path + "\""); } catch { }
            }, "Показать файл обоев в проводнике"));
            row.Children.Add(IconButton("", "УБРАТЬ", "Btn", delegate
            {
                if (selected == null) return;
                var it = selected;
                core.Remove(it);
                FillStrip();
                Select(core.Lib.Items.OrderByDescending(i => i.Added).FirstOrDefault());
                ShowToast("«" + it.Title + "» убраны из коллекции. Сам файл остался на диске.");
            }, "Убрать из коллекции (файл останется на диске)"));
            row.Children.Add(IconButton("", "ВЫКЛЮЧИТЬ", "GhostBtn", delegate { core.TurnOff(); ShowToast("Живые обои выключены — вернулась обычная картинка Windows."); }, "Вернуть обычные обои Windows"));
            sp.Children.Add(row);
            return sp;
        }

        FrameworkElement BuildEmpty()
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560 };
            var ring = new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(42), BorderThickness = new Thickness(2), BorderBrush = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Center };
            ring.Child = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(ring);
            sp.Children.Add(new TextBlock { Text = "Перетащи сюда обои", FontFamily = Theme.Head, FontWeight = FontWeights.SemiBold, FontSize = 30, Margin = new Thickness(0, 22, 0, 10), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = "Видео, GIF, картинки или сцены Wallpaper Engine (scene.pkg). Файлы никуда не копируются — программа только запоминает, где они лежат.", Foreground = Theme.Dim, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 21 });
            return sp;
        }

        Button IconButton(string glyph, string text, string style, RoutedEventHandler click, string tip)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0) });
            row.Children.Add(new TextBlock { Text = Theme.Spaced(text), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = row, Style = (Style)Resources[style], Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(18, 13, 18, 13), ToolTip = tip };
            if (click != null) b.Click += click;
            return b;
        }

        // ================= лента коллекции =================
        FrameworkElement BuildStrip()
        {
            var sp = new StackPanel { Margin = new Thickness(48, 0, 0, 26) };
            var bar = new DockPanel { Margin = new Thickness(0, 0, 40, 14) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = Theme.Spaced("КОЛЛЕКЦИЯ"), FontFamily = Theme.Head, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            countText = new TextBlock { FontFamily = Theme.Head, FontSize = 12, Foreground = Theme.Accent, Margin = new Thickness(9, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(countText);
            foreach (var kv in new[] { Tuple.Create("all", "ВСЕ"), Tuple.Create("scene", "СЦЕНЫ"), Tuple.Create("video", "ВИДЕО"), Tuple.Create("gif", "GIF"), Tuple.Create("image", "ФОТО") })
            {
                var key = kv.Item1;
                var rb = new RadioButton { Content = kv.Item2, GroupName = "filter", Style = (Style)Resources["Chip"], IsChecked = key == "all" };
                rb.Checked += delegate { filterKind = key; ApplyFilter(); };
                title.Children.Add(rb);
            }
            bar.Children.Add(title);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var sGrid = new Grid { Width = 210, Margin = new Thickness(0, 0, 10, 0) };
            search = new TextBox { Style = (Style)Resources["Search"] };
            searchHint = new TextBlock { Text = "Поиск", Foreground = Theme.Faint, Margin = new Thickness(35, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var sIcon = new TextBlock { Text = "", FontFamily = Theme.Icons, FontSize = 12, Foreground = Theme.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), IsHitTestVisible = false };
            search.TextChanged += delegate { searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; ApplyFilter(); };
            sGrid.Children.Add(search); sGrid.Children.Add(sIcon); sGrid.Children.Add(searchHint);
            right.Children.Add(sGrid);
            right.Children.Add(IconButton("", "ДОБАВИТЬ", "Btn", AddFiles, "Добавить видео, GIF, картинки или scene.pkg"));
            right.Children.Add(IconButton("", "ПАПКА", "Btn", AddFolder, "Добавить всё из папки"));
            right.Children.Add(IconButton("", "WALLPAPER ENGINE", "Btn", ImportWe, "Забрать обои из Wallpaper Engine"));
            bar.Children.Add(right);
            sp.Children.Add(bar);

            stripScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 8, 40, 10) };
            stripScroll.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e) { stripScroll.ScrollToHorizontalOffset(stripScroll.HorizontalOffset - e.Delta); e.Handled = true; };
            cardRow = new StackPanel { Orientation = Orientation.Horizontal };
            stripScroll.Content = cardRow;
            sp.Children.Add(stripScroll);
            return sp;
        }

        void FillStrip()
        {
            cardRow.Children.Clear(); cards.Clear();
            int n = 0;
            foreach (var it in core.Lib.Items.OrderByDescending(i => i.Added))
            {
                var c = MakeCard(it, ++n);
                cards[it.Id] = c;
                cardRow.Children.Add(c);
            }
            countText.Text = core.Lib.Items.Count.ToString("00");
            bool empty = core.Lib.Items.Count == 0;
            emptyBlock.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            infoBlock.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
            RefreshCards();
        }

        void ApplyFilter()
        {
            string q = search == null ? "" : search.Text.Trim();
            foreach (var kv in cards)
            {
                var it = (Item)kv.Value.Tag;
                bool ok = (filterKind == "all" || it.Kind == filterKind) && (q.Length == 0 || (it.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                kv.Value.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        Border MakeCard(Item it, int number)
        {
            var outer = new Border { Width = 220, Margin = new Thickness(0, 0, 16, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = it };
            var lift = new TranslateTransform();
            outer.RenderTransform = lift;
            var sp = new StackPanel();

            var zoom = new ScaleTransform(1, 1, 0.5, 0.5);
            var pic = new Border { Height = 124, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1A)) };
            var frame = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Padding = new Thickness(2), Child = pic, Name = "frame" };
            var overlay = new Grid();
            var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(9) };
            badge.Child = new TextBlock { Text = Theme.Spaced(it.Exists ? it.KindLabel : "НЕТ ФАЙЛА"), FontFamily = Theme.Head, FontSize = 9.5, Foreground = it.Exists ? Brushes.White : Theme.Warn };
            overlay.Children.Add(badge);
            var live = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(9), Visibility = Visibility.Collapsed, Name = "live", Background = Theme.Accent };
            live.Child = new TextBlock { Text = Theme.Spaced("НА СТОЛЕ"), FontFamily = Theme.Head, FontSize = 9.5, Foreground = Theme.OnAccent };
            overlay.Children.Add(live);
            pic.Child = overlay;
            sp.Children.Add(frame);

            var under = new DockPanel { Margin = new Thickness(4, 10, 4, 0) };
            var num = new TextBlock { Text = number.ToString("00"), FontFamily = Theme.Head, FontSize = 11, Foreground = Theme.Faint, Margin = new Thickness(0, 1, 9, 0) };
            DockPanel.SetDock(num, Dock.Left);
            under.Children.Add(num);
            under.Children.Add(new TextBlock { Text = it.Title, FontFamily = Theme.Head, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(under);
            outer.Child = sp;

            Thumbs.Get(it, delegate(ImageSource s)
            {
                if (s != null) pic.Background = new ImageBrush(s) { Stretch = Stretch.UniformToFill, RelativeTransform = zoom };
            });

            // без прыжков и зума: при наведении просто проявляется тонкая рамка
            outer.MouseEnter += delegate { if (selected != it) frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)); };
            outer.MouseLeave += delegate { if (selected != it) frame.BorderBrush = Brushes.Transparent; };
            outer.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                Select(it);
                if (e.ClickCount == 2) ApplySelected();
            };
            return outer;
        }

        static IEnumerable<T> Find<T>(DependencyObject root) where T : FrameworkElement
        {
            var list = new List<T>();
            var fe = root as T; if (fe != null) list.Add(fe);
            foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) list.AddRange(Find<T>(c));
            return list;
        }

        void RefreshCards()
        {
            var activeIds = new HashSet<string>();
            if (core.Active)
                foreach (var s in WinForms.Screen.AllScreens) { var it = core.CurrentFor(s.DeviceName); if (it != null) activeIds.Add(it.Id); }
            foreach (var kv in cards)
            {
                foreach (var b in Find<Border>(kv.Value))
                {
                    if (b.Name == "live") b.Visibility = activeIds.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
                    if (b.Name == "frame") b.BorderBrush = selected != null && selected.Id == kv.Key ? Theme.Accent : Brushes.Transparent;
                }
            }
            UpdateApplyButton();
        }

        // ================= выбор и живой фон =================
        void Select(Item it)
        {
            selected = it;
            StopPreview();
            RefreshCards();
            if (it == null) return;

            selTitle.Text = (it.Title ?? "").ToUpperInvariant();
            var parts = new List<string> { it.Kind == "video" ? "ВИДЕО" : it.Kind == "gif" ? "GIF-АНИМАЦИЯ" : it.Kind == "scene" ? "СЦЕНА" : "КАРТИНКА" };
            try { if (it.Exists) parts.Add((new FileInfo(it.Path).Length / (1024.0 * 1024)).ToString("0.0") + " МБ"); } catch { }
            if (it.Source == "we") parts.Add("WALLPAPER ENGINE");
            selMeta.Text = Theme.Spaced(string.Join("  ·  ", parts));
            BuildMonitorRow();
            StartPreview(it);
            previewHost.Opacity = 0;
            previewHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
        }

        void BuildMonitorRow()
        {
            monitorRow.Children.Clear();
            var screens = WinForms.Screen.AllScreens;
            if (screens.Length < 2 || core.Cfg.SameOnAll) { selectedMonitor = null; monitorRow.Visibility = Visibility.Collapsed; return; }
            monitorRow.Visibility = Visibility.Visible;
            var wp = new WrapPanel();
            if (selectedMonitor == null || !screens.Any(s => s.DeviceName == selectedMonitor)) selectedMonitor = screens.First(s => s.Primary).DeviceName;
            int n = 1;
            foreach (var s in screens.OrderBy(s => s.Bounds.Left))
            {
                var name = s.DeviceName;
                var rb = new RadioButton
                {
                    Content = "МОНИТОР " + n + (s.Primary ? " · ОСНОВНОЙ" : "") + "  " + s.Bounds.Width + "×" + s.Bounds.Height,
                    GroupName = "mon", Style = (Style)Resources["Chip"], IsChecked = name == selectedMonitor, Margin = new Thickness(0, 0, 0, 6)
                };
                rb.Checked += delegate { selectedMonitor = name; UpdateApplyButton(); };
                wp.Children.Add(rb); n++;
            }
            monitorRow.Children.Add(wp);
        }

        void UpdateApplyButton()
        {
            if (applyBtn == null || selected == null) return;
            bool exists = selected.Exists;
            var cur = core.CurrentFor(core.Cfg.SameOnAll ? null : selectedMonitor);
            bool on = core.Active && cur != null && cur.Id == selected.Id;
            applyBtn.IsEnabled = exists;
            applyBtn.IsHitTestVisible = exists && !on;
            applyBtn.Style = (Style)Resources[on ? "Btn" : "AccentBtn"]; // уже стоят — спокойная стеклянная кнопка с галочкой
            applyBtn.Padding = new Thickness(26, 14, 26, 14);
            string text = !exists ? "ФАЙЛ НЕ НАЙДЕН" : on ? "УЖЕ НА РАБОЧЕМ СТОЛЕ" : "УСТАНОВИТЬ НА РАБОЧИЙ СТОЛ";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = on ? "" : "", FontFamily = Theme.Icons, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 11, 0), Foreground = on ? Theme.Good : Theme.OnAccent });
            row.Children.Add(new TextBlock { Text = Theme.Spaced(text), VerticalAlignment = VerticalAlignment.Center });
            applyBtn.Content = row;
        }

        void StartPreview(Item it)
        {
            if (!it.Exists) return;
            try
            {
                if (it.Kind == "video")
                {
                    previewVideo = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, IsMuted = true, Stretch = Stretch.UniformToFill, Source = new Uri(it.Path) };
                    var v = previewVideo;
                    v.MediaEnded += delegate { v.Position = TimeSpan.Zero; v.Play(); };
                    previewHost.Children.Add(v);
                    v.Play();
                }
                else if (it.Kind == "scene")
                {
                    previewScene = new SceneView(it.Path, Stretch.UniformToFill);
                    previewHost.Children.Add(previewScene);
                    previewScene.Play();
                }
                else
                {
                    var img = new Image { Stretch = Stretch.UniformToFill };
                    previewHost.Children.Add(img);
                    if (it.Kind == "gif") { previewGif = new GifAnimator(img, it.Path, 1600); previewGif.Play(); }
                    else
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(it.Path); bi.DecodePixelWidth = 1920; bi.EndInit();
                        img.Source = bi;
                    }
                }
            }
            catch (Exception e) { Log.Write(e); }
        }

        void StopPreview()
        {
            if (previewVideo != null) { try { previewVideo.Stop(); previewVideo.Close(); } catch { } previewVideo = null; }
            if (previewGif != null) { previewGif.Dispose(); previewGif = null; }
            if (previewScene != null) { previewScene.Dispose(); previewScene = null; }
            if (previewHost != null) previewHost.Children.Clear();
        }

        void ApplySelected()
        {
            if (selected == null || !selected.Exists) return;
            core.Apply(selected, core.Cfg.SameOnAll ? null : selectedMonitor);
            RefreshCards();
            if (!core.Active) ShowToast("Не получилось встроиться в рабочий стол. " + core.Status);
            else ShowToast("Готово — «" + selected.Title + "» на рабочем столе.");
        }

        // ================= добавление =================
        void AfterImport(Import.Result r)
        {
            core.Lib.Save();
            var newest = core.Lib.Items.OrderByDescending(i => i.Added).FirstOrDefault();
            FillStrip();
            if (r.Added > 0 && newest != null) Select(newest);
            var parts = new List<string>();
            if (r.Added > 0) parts.Add("Добавлено: " + r.Added);
            if (r.Skipped > 0) parts.Add("уже были в коллекции: " + r.Skipped);
            if (r.Scenes > 0) parts.Add("сцены без файлов (" + r.Scenes + ") пропущены");
            if (r.Web > 0) parts.Add("веб-обои (" + r.Web + ") пока не поддерживаются");
            if (parts.Count == 0) parts.Add("Подходящих файлов не нашлось (нужны видео, GIF, картинки или сцены)");
            ShowToast(string.Join(" · ", parts));
        }

        void AddFiles(object s, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true, Title = "Выбери обои",
                Filter = "Обои|*.mp4;*.m4v;*.webm;*.mkv;*.mov;*.avi;*.wmv;*.gif;*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.jfif;scene.pkg;scene.json;project.json|Все файлы|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            var files = dlg.FileNames.Select(f => System.IO.Path.GetFileName(f).Equals("project.json", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetDirectoryName(f) : f).ToArray();
            AfterImport(Import.FromFiles(core.Lib, files));
        }

        void AddFolder(object s, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog { Description = "Папка с обоями (видео, GIF, картинки, сцены WE — включая вложенные папки)", ShowNewFolderButton = false })
                if (dlg.ShowDialog() == WinForms.DialogResult.OK) AfterImport(Import.FromFiles(core.Lib, new[] { dlg.SelectedPath }));
        }

        void ImportWe(object s, RoutedEventArgs e)
        {
            var roots = Import.GuessRoots();
            if (roots.Count == 0)
            {
                using (var dlg = new WinForms.FolderBrowserDialog { Description = "Где лежит Wallpaper Engine? Выбери его папку (или projects\\myprojects, или workshop\\content\\431960)", ShowNewFolderButton = false })
                {
                    if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                    roots.Add(dlg.SelectedPath);
                }
            }
            AfterImport(Import.FromWallpaperEngine(core.Lib, roots));
        }

        void OnDrop(object s, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            AfterImport(Import.FromFiles(core.Lib, files));
        }

        // ================= настройки (выезжающая панель) =================
        void ToggleDrawer(bool open)
        {
            var dim = rootGrid.Children.OfType<Border>().First(b => b.Name == "dim");
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (open)
            {
                drawer.Visibility = Visibility.Visible; dim.Visibility = Visibility.Visible;
                drawerShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(400, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
                dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            }
            else if (drawer.Visibility == Visibility.Visible)
            {
                var a = new DoubleAnimation(0, 400, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
                a.Completed += delegate { drawer.Visibility = Visibility.Collapsed; dim.Visibility = Visibility.Collapsed; };
                drawerShift.BeginAnimation(TranslateTransform.XProperty, a);
                dim.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260)));
            }
        }

        Border BuildDrawer()
        {
            drawerShift = new TranslateTransform(400, 0);
            var d = new Border
            {
                Width = 380, HorizontalAlignment = HorizontalAlignment.Right, Background = Theme.GlassStrong,
                BorderBrush = Theme.GlassLine, BorderThickness = new Thickness(1, 0, 0, 0), RenderTransform = drawerShift, Visibility = Visibility.Collapsed
            };
            var dock = new DockPanel();
            var head = new DockPanel { Margin = new Thickness(24, 16, 14, 4) };
            DockPanel.SetDock(head, Dock.Top);
            var close = CaptionButton("", "Закрыть", delegate { ToggleDrawer(false); });
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(new TextBlock { Text = Theme.Spaced("НАСТРОЙКИ"), FontFamily = Theme.Head, FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            dock.Children.Add(head);

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24, 0, 20, 24) };
            var col = new StackPanel();
            sv.Content = col;
            var cfg = core.Cfg;

            col.Children.Add(Section("ЦВЕТ", "Акцент интерфейса: кнопки, отметки, переключатели.", ColorPicker()));

            col.Children.Add(Section("ПРОИЗВОДИТЕЛЬНОСТЬ", "Когда обоев не видно, они замирают и почти ничего не тратят.",
                Switch("Пауза в играх и полноэкранных программах", "Игра или видео на весь экран — обои замирают", cfg.PauseFullscreen, v => { cfg.PauseFullscreen = v; core.SettingsChanged(); }),
                Switch("Пауза, когда окно развёрнуто", "Окно на весь монитор закрывает обои — незачем их крутить", cfg.PauseMaximized, v => { cfg.PauseMaximized = v; core.SettingsChanged(); }),
                Switch("Пауза, когда открыто любое окно", "Максимальная экономия: обои живут, только когда активен рабочий стол", cfg.PauseFocused, v => { cfg.PauseFocused = v; core.SettingsChanged(); })));

            var fit = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            foreach (var kv in new[] { Tuple.Create("fill", "ЗАПОЛНИТЬ"), Tuple.Create("fit", "ВПИСАТЬ"), Tuple.Create("stretch", "РАСТЯНУТЬ") })
            {
                var key = kv.Item1;
                var rb = new RadioButton { Content = Theme.Spaced(kv.Item2), GroupName = "fit", Style = (Style)Resources["Chip"], IsChecked = cfg.Fit == key };
                rb.Checked += delegate { cfg.Fit = key; core.SettingsChanged(); };
                fit.Children.Add(rb);
            }

            col.Children.Add(Section("ВОСПРОИЗВЕДЕНИЕ", null,
                SliderRow("Громкость видео", 0, 100, cfg.Volume, v => v <= 0 ? "без звука" : v + "%", v => { cfg.Volume = v; core.SettingsChanged(); }),
                SliderRow("Скорость видео", 25, 200, cfg.Speed, v => v + "%", v => { cfg.Speed = v; core.SettingsChanged(); }),
                Label("Масштаб"), fit,
                Switch("Одни обои на всех мониторах", "Выключи, чтобы ставить разные обои на каждый монитор", cfg.SameOnAll, v => { cfg.SameOnAll = v; core.SettingsChanged(); core.Rebuild(); BuildMonitorRow(); UpdateApplyButton(); })));

            col.Children.Add(Section("СИСТЕМА", null,
                Switch("Запускать вместе с Windows", "Обои включаются сами при старте компьютера", Core.Autostart, v => { Core.Autostart = v; ShowToast(v ? "Готово — Backdrop будет запускаться вместе с Windows." : "Автозапуск выключен."); }),
                Switch("Показывать окно при включении компьютера", "Если выключено — программа тихо сидит в трее, окно открывается по значку", cfg.ShowOnStartup, v => { cfg.ShowOnStartup = v; cfg.Save(); })));

            col.Children.Add(new TextBlock
            {
                Text = "Backdrop " + Program.Version + " — бесплатно и с открытым кодом. Файлы обоев никуда не копируются: программа только читает их с того места, где они лежат.",
                Foreground = Theme.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), LineHeight = 19
            });
            dock.Children.Add(sv);
            d.Child = dock;
            return d;
        }

        public void OpenSettings()
        {
            drawer.Visibility = Visibility.Visible;
            drawerShift.X = 0;
            var dim = rootGrid.Children.OfType<Border>().First(b => b.Name == "dim");
            dim.Visibility = Visibility.Visible; dim.Opacity = 1;
        }

        // Выбор акцентного цвета: готовые цвета + свой код
        FrameworkElement ColorPicker()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var wp = new WrapPanel();
            foreach (var hex in Theme.Presets)
            {
                var h = hex;
                bool cur = string.Equals(h, Theme.AccentHex, StringComparison.OrdinalIgnoreCase);
                var sw = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)),
                    BorderThickness = new Thickness(cur ? 3 : 0), BorderBrush = Theme.Text, ToolTip = h
                };
                sw.MouseLeftButtonUp += delegate { ApplyAccent(h); };
                wp.Children.Add(sw);
            }
            sp.Children.Add(wp);

            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var ok = IconButton("", "ПРИМЕНИТЬ", "Btn", null, "Применить свой цвет");
            DockPanel.SetDock(ok, Dock.Right);
            var box = new TextBox { Style = (Style)Resources["Search"], Text = Theme.AccentHex, Margin = new Thickness(0, 0, 10, 0) };
            var swatch = new Ellipse { Width = 14, Height = 14, Fill = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0), IsHitTestVisible = false };
            box.TextChanged += delegate
            {
                try { swatch.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(box.Text.Trim().StartsWith("#") ? box.Text.Trim() : "#" + box.Text.Trim())); } catch { }
            };
            ok.Click += delegate { var t = box.Text.Trim(); ApplyAccent(t.StartsWith("#") ? t : "#" + t); };
            box.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { var t = box.Text.Trim(); ApplyAccent(t.StartsWith("#") ? t : "#" + t); } };
            row.Children.Add(ok);
            var g = new Grid();
            g.Children.Add(box); g.Children.Add(swatch);
            row.Children.Add(g);
            sp.Children.Add(row);
            sp.Children.Add(new TextBlock { Text = "Свой цвет — в формате #RRGGBB, например #FF6B4A", Foreground = Theme.Faint, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0) });
            return sp;
        }

        void ApplyAccent(string hex)
        {
            if (!Theme.SetAccent(hex)) { ShowToast("Не понял цвет «" + hex + "». Нужно так: #FF6B4A"); return; }
            core.Cfg.Accent = Theme.AccentHex;
            core.Cfg.Save();
            core.ReopenUi();
        }

        FrameworkElement Section(string title, string sub, params FrameworkElement[] items)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 6) };
            var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            h.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Theme.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            h.Children.Add(new TextBlock { Text = Theme.Spaced(title), FontFamily = Theme.Head, FontWeight = FontWeights.SemiBold, FontSize = 11 });
            sp.Children.Add(h);
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, Foreground = Theme.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
            foreach (var i in items) sp.Children.Add(i);
            return sp;
        }

        FrameworkElement Label(string t)
        {
            return new TextBlock { Text = t, FontSize = 12, Margin = new Thickness(0, 10, 0, 6) };
        }

        FrameworkElement Switch(string title, string sub, bool value, Action<bool> changed)
        {
            var cb = new CheckBox { Style = (Style)Resources["Switch"], IsChecked = value };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, Foreground = Theme.Faint, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            cb.Content = sp;
            cb.Checked += delegate { changed(true); };
            cb.Unchecked += delegate { changed(false); };
            return cb;
        }

        FrameworkElement SliderRow(string title, int min, int max, int value, Func<int, string> fmt, Action<int> changed)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
            var top = new DockPanel();
            var val = new TextBlock { Text = fmt(value), Foreground = Theme.Dim, FontFamily = Theme.Head };
            DockPanel.SetDock(val, Dock.Right);
            top.Children.Add(val);
            top.Children.Add(new TextBlock { Text = title, FontSize = 12 });
            sp.Children.Add(top);
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, Style = (Style)Resources["Sl"], SmallChange = 5, LargeChange = 5, IsSnapToTickEnabled = true, TickFrequency = 5, Margin = new Thickness(0, 6, 0, 0) };
            sl.ValueChanged += delegate { int v = (int)Math.Round(sl.Value); val.Text = fmt(v); changed(v); };
            sp.Children.Add(sl);
            return sp;
        }
    }
}
