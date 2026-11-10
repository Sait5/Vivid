using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Backdrop
{
    // Частицы сцены WE (пепел, снег, искры, пыль): эмиттеры, инициализаторы и операторы в 2D.
    class ParticleSystem
    {
        public class P
        {
            public double X, Y, Vx, Vy, Rot, Av, Size, Life, Age, Alpha, Turb, Seed;
            public int Frame, Tint;
        }

        public double Rate { get { return rate; } }
        public int MaxCount { get { return maxCount; } }
        public double StartTime { get { return startTime; } }
        public double Height { get { return H; } }
        public bool Sequence { get { return sequence; } }
        public BitmapSource[][] Frames { get { return frames; } }

        // Для композитора: новая частица без добавления в общий список
        public P Create()
        {
            int before = live.Count;
            Spawn();
            var p = live[live.Count - 1];
            live.RemoveAt(live.Count - 1);
            return p;
        }

        // Шаг одной частицы (то же, что в Step)
        public void Advance(P p, double dt, double t)
        {
            double damp = Math.Max(0, 1 - drag * dt);
            p.Age += dt;
            p.Vx = (p.Vx + gravity[0] * dt) * damp;
            p.Vy = (p.Vy + gravity[1] * dt) * damp;
            double mx = p.Vx, my = p.Vy;
            if (turbulence)
            {
                double a = Noise(p.X * turbScale, p.Y * turbScale, t * turbTime * 0.1 + p.Seed * 0.001) * Math.PI * 4;
                mx += Math.Cos(a) * p.Turb; my += Math.Sin(a) * p.Turb;
            }
            p.X += mx * dt; p.Y += my * dt;
            p.Av += avForce * dt;
            p.Rot += p.Av * dt;
        }

        public double FadeAt(double f, double alpha)
        {
            double a = alpha;
            if (fadeIn >= 0)
            {
                if (f < fadeIn && fadeIn > 0) a *= f / fadeIn;
                else if (f > fadeOut && fadeOut < 1) a *= Math.Max(0, 1 - (f - fadeOut) / (1 - fadeOut));
            }
            return Math.Max(0, Math.Min(1, a));
        }

        public double SizeAt(double f) { return sizeChange ? Lerp(sizeStart, sizeEnd, f) : 1; }
        public double FadeInEnd { get { return fadeIn >= 0 ? fadeIn : 0; } }
        public double FadeOutStart { get { return fadeIn >= 0 ? fadeOut : 1; } }

        public readonly FrameworkElement Host;
        readonly DrawingVisual visual = new DrawingVisual();
        readonly List<P> live = new List<P>();
        readonly Random rnd = new Random();
        readonly double H;
        double spawnAcc, time;

        // параметры
        int maxCount = 100; double rate = 10, startTime;
        string emitterKind = "sphererandom";
        double[] emOrigin = { 0, 0, 0 }, emDir = { 1, 1, 1 }, boxMin = { 0, 0, 0 }, boxMax = { 0, 0, 0 };
        double distMin, distMax = 100;
        double lifeMin = 1, lifeMax = 1, sizeMin = 20, sizeMax = 20, alphaMin = 1, alphaMax = 1;
        double[] velMin = { 0, 0, 0 }, velMax = { 0, 0, 0 };
        double[] colMin = { 255, 255, 255 }, colMax = { 255, 255, 255 };
        double rotMin, rotMax, avMin, avMax;
        double[] gravity = { 0, 0, 0 }; double drag;
        double fadeIn = -1, fadeOut = -1;
        double sizeStart = 1, sizeEnd = 1; bool sizeChange;
        double turbMin, turbMax, turbScale = 0.01, turbTime = 1; bool turbulence;
        double avForce;
        bool sequence;
        double ox, oy, sx = 1, sy = 1;

        BitmapSource[][] frames; // [оттенок][кадр]

        class HostElement : FrameworkElement
        {
            readonly Visual v;
            public HostElement(Visual v) { this.v = v; AddVisualChild(v); IsHitTestVisible = false; }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int i) { return v; }
        }

        public ParticleSystem(Dictionary<string, object> def, Dictionary<string, object> over, double[] origin, double[] scale, TexImage tex, Dictionary<string, object> sheet, double w, double h)
        {
            H = h;
            ox = origin[0]; oy = origin[1]; sx = scale[0]; sy = scale[1];
            Host = new HostElement(visual) { Width = w, Height = h };

            maxCount = (int)J.Num(def, "maxcount", 100);
            startTime = J.Num(def, "starttime", 0);
            sequence = (J.Str(def, "animationmode") ?? "") == "sequence";

            var em = J.List(def, "emitter").FirstOrDefault();
            if (em != null)
            {
                emitterKind = J.Str(em, "name") ?? emitterKind;
                rate = J.Num(em, "rate", rate);
                emOrigin = J.Vec(em, "origin", emOrigin);
                emDir = J.Vec(em, "directions", emDir);
                distMin = J.Num(em, "distancemin", 0); distMax = J.Num(em, "distancemax", distMax);
                boxMin = J.Vec(em, "distancemin", boxMin); boxMax = J.Vec(em, "distancemax", boxMax);
            }
            foreach (var i in J.List(def, "initializer"))
            {
                switch (J.Str(i, "name"))
                {
                    case "lifetimerandom": lifeMin = J.Num(i, "min", 1); lifeMax = J.Num(i, "max", 1); break;
                    case "sizerandom": sizeMin = J.Num(i, "min", 20); sizeMax = J.Num(i, "max", 20); break;
                    case "alpharandom": alphaMin = J.Num(i, "min", 1); alphaMax = J.Num(i, "max", 1); break;
                    case "velocityrandom": velMin = J.Vec(i, "min", velMin); velMax = J.Vec(i, "max", velMax); break;
                    case "colorrandom": colMin = J.Vec(i, "min", colMin); colMax = J.Vec(i, "max", colMax); break;
                    case "rotationrandom": rotMin = J.Vec(i, "min", new double[3])[2]; rotMax = J.Vec(i, "max", new double[3])[2]; break;
                    case "angularvelocityrandom": avMin = J.Vec(i, "min", new double[3])[2]; avMax = J.Vec(i, "max", new double[3])[2]; break;
                }
            }
            foreach (var o in J.List(def, "operator"))
            {
                switch (J.Str(o, "name"))
                {
                    case "movement": gravity = J.Vec(o, "gravity", gravity); drag = J.Num(o, "drag", 0); break;
                    case "alphafade": fadeIn = J.Num(o, "fadeintime", 0.5); fadeOut = J.Num(o, "fadeouttime", 0.5); break;
                    case "sizechange": sizeChange = true; sizeStart = J.Num(o, "startvalue", 1); sizeEnd = J.Num(o, "endvalue", 0); break;
                    case "turbulence":
                        turbulence = true; turbMin = J.Num(o, "speedmin", 0); turbMax = J.Num(o, "speedmax", 100);
                        turbScale = J.Num(o, "scale", 0.01); turbTime = J.Num(o, "timescale", 1); break;
                    case "angularmovement": avForce = J.Vec(o, "force", new double[3])[2]; break;
                }
            }
            if (over != null)
            {
                rate *= J.Num(over, "rate", 1);
                maxCount = (int)Math.Round(maxCount * J.Num(over, "count", 1));
                double sm = J.Num(over, "size", 1); sizeMin *= sm; sizeMax *= sm;
                double lm = J.Num(over, "lifetime", 1); lifeMin *= lm; lifeMax *= lm;
                double vm = J.Num(over, "speed", 1); for (int k = 0; k < 3; k++) { velMin[k] *= vm; velMax[k] *= vm; }
                double am = J.Num(over, "alpha", 1); alphaMin *= am; alphaMax *= am;
                var cn = J.Vec(over, "colorn", null);
                if (cn != null) for (int k = 0; k < 3; k++) { colMin[k] = colMax[k] = cn[k] * 255; }
            }
            maxCount = Math.Max(1, Math.Min(maxCount, 4000));
            BuildFrames(tex, sheet);
        }

        double R(double a, double b) { return a + (b - a) * rnd.NextDouble(); }

        void BuildFrames(TexImage tex, Dictionary<string, object> sheet)
        {
            // кадры спрайт-листа
            var cut = new List<BitmapSource>();
            BitmapSource src = tex != null ? tex.Bitmap : SoftDot();
            int fw = src.PixelWidth, fh = src.PixelHeight, count = 1;
            var seq = J.List(sheet, "spritesheetsequences").FirstOrDefault();
            if (seq != null)
            {
                fw = (int)J.Num(seq, "width", fw); fh = (int)J.Num(seq, "height", fh); count = (int)J.Num(seq, "frames", 1);
            }
            int cols = Math.Max(1, src.PixelWidth / Math.Max(1, fw));
            for (int i = 0; i < count; i++)
            {
                int x = (i % cols) * fw, y = (i / cols) * fh;
                if (x + fw > src.PixelWidth || y + fh > src.PixelHeight) break;
                cut.Add(count == 1 && fw == src.PixelWidth && fh == src.PixelHeight ? src : new CroppedBitmap(src, new Int32Rect(x, y, fw, fh)));
            }
            if (cut.Count == 0) cut.Add(src);

            // цвет частиц «запекаем» в несколько оттенков (дешевле, чем маска на каждую частицу)
            bool mask = tex == null || tex.IsMask;
            int tints = mask ? 6 : 1;
            frames = new BitmapSource[tints][];
            for (int t = 0; t < tints; t++)
            {
                double f = tints == 1 ? 0 : (double)t / (tints - 1);
                byte r = (byte)Math.Max(0, Math.Min(255, Lerp(Math.Min(colMin[0], colMax[0]), Math.Max(colMin[0], colMax[0]), Frac(f, 0))));
                byte g = (byte)Math.Max(0, Math.Min(255, Lerp(Math.Min(colMin[1], colMax[1]), Math.Max(colMin[1], colMax[1]), Frac(f, 1))));
                byte b = (byte)Math.Max(0, Math.Min(255, Lerp(Math.Min(colMin[2], colMax[2]), Math.Max(colMin[2], colMax[2]), Frac(f, 2))));
                frames[t] = cut.Select(c => mask ? Tint(c, r, g, b) : c).ToArray();
            }
        }

        double Frac(double f, int ch) { return (f * (3 + ch) + ch * 0.37) % 1.0; }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        static BitmapSource Tint(BitmapSource src, byte r, byte g, byte b)
        {
            var s = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = s.PixelWidth, h = s.PixelHeight;
            var px = new byte[w * h * 4];
            s.CopyPixels(px, w * 4, 0);
            for (int i = 0; i < px.Length; i += 4)
            {
                int a = px[i + 3];
                // премультиплицированный цвет для Pbgra32
                px[i] = (byte)(b * a / 255); px[i + 1] = (byte)(g * a / 255); px[i + 2] = (byte)(r * a / 255);
            }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            bmp.Freeze();
            return bmp;
        }

        static BitmapSource SoftDot()
        {
            int n = 64; var px = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double d = Math.Sqrt((x - n / 2.0 + 0.5) * (x - n / 2.0 + 0.5) + (y - n / 2.0 + 0.5) * (y - n / 2.0 + 0.5)) / (n / 2.0);
                    byte a = (byte)(Math.Max(0, 1 - d) * Math.Max(0, 1 - d) * 255);
                    int i = (y * n + x) * 4; px[i] = px[i + 1] = px[i + 2] = 255; px[i + 3] = a;
                }
            var b = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
            b.Freeze();
            return b;
        }

        void Spawn()
        {
            var p = new P();
            double px = 0, py = 0;
            if (emitterKind == "boxrandom")
            {
                px = R(-boxMax[0], boxMax[0]); py = R(-boxMax[1], boxMax[1]);
                if (Math.Abs(px) < boxMin[0] && Math.Abs(py) < boxMin[1]) px = Math.Sign(px) * boxMin[0];
            }
            else
            {
                // случайная точка в сфере, сжатой по осям (directions)
                double z, dx, dy, dz, len;
                do { dx = R(-1, 1); dy = R(-1, 1); dz = R(-1, 1); len = dx * dx + dy * dy + dz * dz; } while (len > 1 || len < 1e-6);
                len = Math.Sqrt(len);
                double dist = R(distMin, distMax);
                px = dx / len * dist * emDir[0]; py = dy / len * dist * emDir[1]; z = dz;
            }
            p.X = ox + (emOrigin[0] + px) * sx;
            p.Y = oy + (emOrigin[1] + py) * sy;
            p.Vx = R(velMin[0], velMax[0]); p.Vy = R(velMin[1], velMax[1]);
            p.Life = Math.Max(0.05, R(lifeMin, lifeMax));
            p.Size = R(sizeMin, sizeMax) * Math.Abs(sx);
            p.Alpha = R(alphaMin, alphaMax);
            p.Rot = R(rotMin, rotMax); p.Av = R(avMin, avMax);
            p.Turb = R(turbMin, turbMax); p.Seed = rnd.NextDouble() * 1000;
            p.Tint = rnd.Next(frames.Length);
            p.Frame = rnd.Next(frames[p.Tint].Length);
            live.Add(p);
        }

        public void Prewarm()
        {
            double t = Math.Min(startTime, 30);
            while (t > 0) { Step(1 / 30.0); t -= 1 / 30.0; }
        }

        public void Step(double dt)
        {
            time += dt;
            spawnAcc += rate * dt;
            while (spawnAcc >= 1) { spawnAcc -= 1; if (live.Count < maxCount) Spawn(); }
            double damp = Math.Max(0, 1 - drag * dt);
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                p.Age += dt;
                if (p.Age >= p.Life) { live.RemoveAt(i); continue; }
                p.Vx = (p.Vx + gravity[0] * dt) * damp;
                p.Vy = (p.Vy + gravity[1] * dt) * damp;
                double mx = p.Vx, my = p.Vy;
                if (turbulence)
                {
                    double a = Noise(p.X * turbScale, p.Y * turbScale, time * turbTime * 0.1 + p.Seed * 0.001) * Math.PI * 4;
                    mx += Math.Cos(a) * p.Turb; my += Math.Sin(a) * p.Turb;
                }
                p.X += mx * dt; p.Y += my * dt;
                p.Av += avForce * dt;
                p.Rot += p.Av * dt;
            }
        }

        public void Render()
        {
            using (var dc = visual.RenderOpen())
            {
                foreach (var p in live)
                {
                    double f = p.Age / p.Life, a = p.Alpha;
                    if (fadeIn >= 0)
                    {
                        if (f < fadeIn && fadeIn > 0) a *= f / fadeIn;
                        else if (f > fadeOut && fadeOut < 1) a *= Math.Max(0, 1 - (f - fadeOut) / (1 - fadeOut));
                    }
                    if (a <= 0.01) continue;
                    double size = p.Size * (sizeChange ? Lerp(sizeStart, sizeEnd, f) : 1);
                    if (size < 0.5) continue;
                    var set = frames[p.Tint];
                    var img = set[sequence ? Math.Min(set.Length - 1, (int)(f * set.Length)) : p.Frame];
                    double cx = p.X, cy = H - p.Y;
                    var m = Matrix.Identity;
                    m.Translate(-size / 2, -size / 2);
                    m.Rotate(-p.Rot * 180 / Math.PI);
                    m.Translate(cx, cy);
                    dc.PushTransform(new MatrixTransform(m));
                    dc.PushOpacity(Math.Min(1, a));
                    dc.DrawImage(img, new Rect(0, 0, size, size));
                    dc.Pop(); dc.Pop();
                }
            }
        }

        // Плавный «шум» для турбулентности
        static double Hash(int x, int y, int z)
        {
            int h = x * 374761393 + y * 668265263 + z * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffff) / 65535.0;
        }

        static double Smooth(double t) { return t * t * (3 - 2 * t); }

        static double Noise(double x, double y, double z)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
            double xf = Smooth(x - xi), yf = Smooth(y - yi), zf = Smooth(z - zi);
            double a = Lerp(Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), xf), Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), xf), yf);
            double b = Lerp(Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), xf), Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), xf), yf);
            return Lerp(a, b, zf);
        }
    }
}
