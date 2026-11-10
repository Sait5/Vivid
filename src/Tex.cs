using System;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vivid
{
    // Чтение текстур Wallpaper Engine (.tex): вложенные PNG/JPG, сырые RGBA/R8/RG88, DXT1/3/5, сжатие LZ4.
    class TexImage
    {
        public BitmapSource Bitmap;
        public bool IsMask;          // R8: одноканальная маска (рисуется цветом частицы)
        public byte[] Mp4;           // текстура-видео (бывает в новых сценах)
        public int Width, Height;

        static string ReadTag(BinaryReader r)
        {
            var sb = new StringBuilder();
            int c;
            while ((c = r.ReadByte()) != 0) sb.Append((char)c);
            return sb.ToString();
        }

        public static TexImage Load(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                string v = ReadTag(r), i = ReadTag(r);
                if (!v.StartsWith("TEXV") || !i.StartsWith("TEXI")) throw new InvalidDataException("не текстура WE");
                int format = r.ReadInt32(); r.ReadInt32(); // flags
                int texW = r.ReadInt32(), texH = r.ReadInt32(), imgW = r.ReadInt32(), imgH = r.ReadInt32();
                r.ReadUInt32();
                string b = ReadTag(r);
                int ver = int.Parse(b.Substring(4));
                int imageCount = r.ReadInt32();
                int fif = -1; bool mp4 = false;
                if (ver >= 3) fif = r.ReadInt32();
                if (ver >= 4) mp4 = r.ReadInt32() != 0;
                if (imageCount < 1) throw new InvalidDataException("пустая текстура");
                int mips = r.ReadInt32();
                int w = r.ReadInt32(), h = r.ReadInt32();
                bool compressed = false; int raw = 0;
                if (ver >= 2) { compressed = r.ReadInt32() != 0; raw = r.ReadInt32(); }
                int size = r.ReadInt32();
                byte[] bytes = r.ReadBytes(size);
                if (compressed) bytes = Lz4.Decode(bytes, raw);

                var t = new TexImage();
                if (imgW <= 0) imgW = w; if (imgH <= 0) imgH = h;
                if (mp4) { t.Mp4 = bytes; t.Width = imgW; t.Height = imgH; return t; }
                if (fif != -1)
                {
                    var dec = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    t.Bitmap = dec.Frames[0];
                }
                else
                {
                    byte[] bgra = Decode(format, bytes, w, h);
                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
                    if (imgW < w || imgH < h) bmp = new CroppedBitmap(bmp, new System.Windows.Int32Rect(0, 0, Math.Min(imgW, w), Math.Min(imgH, h)));
                    t.Bitmap = bmp;
                    t.IsMask = format == 9;
                }
                t.Bitmap.Freeze();
                t.Width = t.Bitmap.PixelWidth; t.Height = t.Bitmap.PixelHeight;
                return t;
            }
        }

        static byte[] Decode(int format, byte[] s, int w, int h)
        {
            var o = new byte[w * h * 4];
            switch (format)
            {
                case 0: // RGBA8888
                    for (int p = 0; p < w * h && p * 4 + 3 < s.Length; p++)
                    { o[p * 4] = s[p * 4 + 2]; o[p * 4 + 1] = s[p * 4 + 1]; o[p * 4 + 2] = s[p * 4]; o[p * 4 + 3] = s[p * 4 + 3]; }
                    break;
                case 9: // R8 — маска: белый цвет, альфа = яркость
                    for (int p = 0; p < w * h && p < s.Length; p++) { o[p * 4] = o[p * 4 + 1] = o[p * 4 + 2] = 255; o[p * 4 + 3] = s[p]; }
                    break;
                case 8: // RG88 — яркость + альфа
                    for (int p = 0; p < w * h && p * 2 + 1 < s.Length; p++) { o[p * 4] = o[p * 4 + 1] = o[p * 4 + 2] = s[p * 2]; o[p * 4 + 3] = s[p * 2 + 1]; }
                    break;
                case 7: Dxt(s, w, h, o, 1); break;
                case 6: Dxt(s, w, h, o, 3); break;
                case 4: Dxt(s, w, h, o, 5); break;
                default: throw new NotSupportedException("формат текстуры " + format);
            }
            return o;
        }

        static void Color565(int c, out int r, out int g, out int b)
        {
            r = ((c >> 11) & 31) * 255 / 31; g = ((c >> 5) & 63) * 255 / 63; b = (c & 31) * 255 / 31;
        }

        static void Dxt(byte[] s, int w, int h, byte[] o, int kind)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4, blockSize = kind == 1 ? 8 : 16, pos = 0;
            var cr = new int[4]; var cg = new int[4]; var cb = new int[4]; var ca = new int[4];
            var alpha = new int[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++, pos += blockSize)
                {
                    if (pos + blockSize > s.Length) return;
                    int cpos = pos;
                    for (int k = 0; k < 16; k++) alpha[k] = 255;
                    if (kind == 3)
                    {
                        for (int k = 0; k < 16; k++) { int v = (s[pos + k / 2] >> ((k & 1) * 4)) & 15; alpha[k] = v * 17; }
                        cpos += 8;
                    }
                    else if (kind == 5)
                    {
                        int a0 = s[pos], a1 = s[pos + 1];
                        var pal = new int[8]; pal[0] = a0; pal[1] = a1;
                        if (a0 > a1) for (int k = 1; k < 7; k++) pal[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                        else { for (int k = 1; k < 5; k++) pal[k + 1] = ((5 - k) * a0 + k * a1) / 5; pal[6] = 0; pal[7] = 255; }
                        ulong bits = 0;
                        for (int k = 0; k < 6; k++) bits |= (ulong)s[pos + 2 + k] << (8 * k);
                        for (int k = 0; k < 16; k++) alpha[k] = pal[(int)((bits >> (3 * k)) & 7)];
                        cpos += 8;
                    }
                    int c0 = s[cpos] | (s[cpos + 1] << 8), c1 = s[cpos + 2] | (s[cpos + 3] << 8);
                    Color565(c0, out cr[0], out cg[0], out cb[0]); Color565(c1, out cr[1], out cg[1], out cb[1]);
                    ca[0] = ca[1] = ca[2] = ca[3] = 255;
                    if (c0 > c1 || kind != 1)
                    {
                        cr[2] = (2 * cr[0] + cr[1]) / 3; cg[2] = (2 * cg[0] + cg[1]) / 3; cb[2] = (2 * cb[0] + cb[1]) / 3;
                        cr[3] = (cr[0] + 2 * cr[1]) / 3; cg[3] = (cg[0] + 2 * cg[1]) / 3; cb[3] = (cb[0] + 2 * cb[1]) / 3;
                    }
                    else
                    {
                        cr[2] = (cr[0] + cr[1]) / 2; cg[2] = (cg[0] + cg[1]) / 2; cb[2] = (cb[0] + cb[1]) / 2;
                        cr[3] = cg[3] = cb[3] = 0; ca[3] = 0;
                    }
                    uint idx = (uint)(s[cpos + 4] | (s[cpos + 5] << 8) | (s[cpos + 6] << 16) | (s[cpos + 7] << 24));
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                        if (x >= w || y >= h) continue;
                        int ci = (int)((idx >> (2 * k)) & 3), p = (y * w + x) * 4;
                        o[p] = (byte)cb[ci]; o[p + 1] = (byte)cg[ci]; o[p + 2] = (byte)cr[ci];
                        o[p + 3] = (byte)(kind == 1 ? ca[ci] : alpha[k]);
                    }
                }
        }
    }

    static class Lz4
    {
        // Распаковка блока LZ4 (без заголовка кадра)
        public static byte[] Decode(byte[] src, int outLen)
        {
            var dst = new byte[outLen];
            int s = 0, d = 0;
            while (s < src.Length)
            {
                int token = src[s++];
                int lit = token >> 4;
                if (lit == 15) { int b; do { b = src[s++]; lit += b; } while (b == 255); }
                Buffer.BlockCopy(src, s, dst, d, Math.Min(lit, outLen - d)); s += lit; d += lit;
                if (s >= src.Length || d >= outLen) break;
                int off = src[s] | (src[s + 1] << 8); s += 2;
                int len = token & 15;
                if (len == 15) { int b; do { b = src[s++]; len += b; } while (b == 255); }
                len += 4;
                int from = d - off;
                for (int k = 0; k < len && d < outLen; k++) dst[d++] = dst[from + k];
            }
            return dst;
        }
    }
}
