using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// Иконка Backdrop: «обои в окне» — тёмная плитка, оранжевый закатный экран, бледный полумесяц, тёмные холмы.
// Рисуется средствами WPF (мягкое свечение и объём), сохраняется как .ico (PNG внутри) + app.png.
static class MakeIcon
{
    static Color C(string h) { return (Color)ColorConverter.ConvertFromString(h); }

    static UIElement Build(bool small)
    {
        var root = new Grid { Width = 256, Height = 256 };
        // плитка
        root.Children.Add(new Border
        {
            Margin = new Thickness(6), CornerRadius = new CornerRadius(58),
            Background = new LinearGradientBrush(C("#1A191D"), C("#0B0B0D"), 90),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(60, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90),
            BorderThickness = new Thickness(small ? 0 : 2)
        });

        var c = new Canvas();
        root.Children.Add(c);
        var screen = new Rect(small ? 30 : 40, small ? 52 : 62, small ? 196 : 176, small ? 152 : 132);

        // тёплое свечение под экраном (объём)
        if (!small)
        {
            var glow = new Rectangle { Width = screen.Width - 20, Height = screen.Height - 30, RadiusX = 30, RadiusY = 30, Fill = new SolidColorBrush(C("#FF6A2E")), Opacity = 0.55, Effect = new BlurEffect { Radius = 34 } };
            Canvas.SetLeft(glow, screen.X + 10); Canvas.SetTop(glow, screen.Y + 34);
            c.Children.Add(glow);
        }

        // экран: закатное небо одного тона
        var frame = new RectangleGeometry(screen, small ? 30 : 26, small ? 30 : 26);
        var sky = new Path
        {
            Data = frame,
            Fill = new LinearGradientBrush(C("#FF9A5A"), C("#FF5A2C"), 90)
        };
        c.Children.Add(sky);

        // всё внутри экрана обрезаем по его форме
        var inside = new Canvas { Clip = frame };
        c.Children.Add(inside);

        // полумесяц
        double mx = screen.X + screen.Width * 0.66, my = screen.Y + screen.Height * 0.34, mr = screen.Height * 0.2;
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(mx, my), mr, mr),
            new EllipseGeometry(new Point(mx + mr * 0.48, my - mr * 0.34), mr * 0.86, mr * 0.86));
        if (!small) inside.Children.Add(new Path { Data = moon, Fill = new SolidColorBrush(C("#FFF3E6")), Opacity = 0.7, Effect = new BlurEffect { Radius = 14 } });
        inside.Children.Add(new Path { Data = moon, Fill = new SolidColorBrush(C("#FFF3E6")) });

        // звёзды
        if (!small)
            foreach (var s in new[] { new Point(0.22, 0.24), new Point(0.38, 0.14), new Point(0.86, 0.62) })
            {
                var d = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromArgb(220, 255, 240, 225)) };
                Canvas.SetLeft(d, screen.X + screen.Width * s.X); Canvas.SetTop(d, screen.Y + screen.Height * s.Y);
                inside.Children.Add(d);
            }

        // холмы
        double b = screen.Bottom, l = screen.Left, w = screen.Width, h = screen.Height;
        string far = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "M {0},{1} C {2},{3} {4},{5} {6},{7} C {8},{9} {10},{11} {12},{13} L {12},{14} L {0},{14} Z",
            l, b - h * 0.30, l + w * 0.25, b - h * 0.52, l + w * 0.45, b - h * 0.40, l + w * 0.62, b - h * 0.34,
            l + w * 0.78, b - h * 0.28, l + w * 0.9, b - h * 0.42, l + w, b - h * 0.40, b + 2);
        string near = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "M {0},{1} C {2},{3} {4},{5} {6},{7} C {8},{9} {10},{11} {12},{13} L {12},{14} L {0},{14} Z",
            l, b - h * 0.14, l + w * 0.2, b - h * 0.30, l + w * 0.42, b - h * 0.26, l + w * 0.6, b - h * 0.16,
            l + w * 0.75, b - h * 0.08, l + w * 0.88, b - h * 0.20, l + w, b - h * 0.22, b + 2);
        inside.Children.Add(new Path { Data = Geometry.Parse(far), Fill = new SolidColorBrush(C("#7A2A1A")) });
        inside.Children.Add(new Path { Data = Geometry.Parse(near), Fill = new SolidColorBrush(C("#140C0C")) });

        // тонкая светлая кромка экрана
        if (!small) c.Children.Add(new Path { Data = frame, Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 230, 210)), StrokeThickness = 2 });
        return root;
    }

    static byte[] Png(int size)
    {
        var el = Build(size < 40);
        var vb = new Viewbox { Width = size, Height = size, Child = el };
        vb.Measure(new Size(size, size)); vb.Arrange(new Rect(0, 0, size, size)); vb.UpdateLayout();
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(vb);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var ms = new System.IO.MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : ".";
        System.IO.Directory.CreateDirectory(outDir);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes) pngs.Add(Png(s));
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "app.png"), Png(512));
        using (var fs = System.IO.File.Create(System.IO.Path.Combine(outDir, "app.ico")))
        using (var w = new System.IO.BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset); offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        Console.WriteLine("icon ok");
        return 0;
    }
}
