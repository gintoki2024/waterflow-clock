using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class Wallpaper : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string path);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string id);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        NativeRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string id);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
    }

    private sealed class Surface
    {
        public Rectangle Bounds;
        public int Width, Height;
        public int[] Pixels;
    }

    private List<Surface> surfaces = new List<Surface>();
    private Color background = Color.FromArgb(24, 28, 36);
    public int Version { get; private set; }

    public Wallpaper() { Refresh(); }

    public void Refresh()
    {
        var result = new List<Surface>();
        IDesktopWallpaper service = null;
        try
        {
            Type type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
            service = (IDesktopWallpaper)Activator.CreateInstance(type);
            uint rgb = service.GetBackgroundColor();
            background = Color.FromArgb((int)(rgb & 255), (int)((rgb >> 8) & 255), (int)((rgb >> 16) & 255));
            int position = service.GetPosition();
            uint count = service.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = service.GetMonitorDevicePathAt(i);
                NativeRect rect = service.GetMonitorRECT(id);
                Rectangle bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                string path = service.GetWallpaper(id);
                if (bounds.Width > 0 && bounds.Height > 0 && File.Exists(path))
                    result.Add(Render(path, bounds, position));
            }
        }
        catch (Exception)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            {
                if (key != null)
                {
                    string path = Convert.ToString(key.GetValue("WallPaper"));
                    int position;
                    switch (Convert.ToString(key.GetValue("WallpaperStyle")))
                    {
                        case "2": position = 2; break;
                        case "6": position = 3; break;
                        case "10": position = 4; break;
                        case "22": position = 5; break;
                        default: position = Convert.ToString(key.GetValue("TileWallpaper")) == "1" ? 1 : 0; break;
                    }
                    if (File.Exists(path))
                    {
                        foreach (Screen monitor in Screen.AllScreens)
                        {
                            try { result.Add(Render(path, monitor.Bounds, position)); }
                            catch (IOException) { }
                            catch (ArgumentException) { }
                            catch (OutOfMemoryException) { }
                        }
                    }
                }
            }
        }
        finally
        {
            if (service != null) Marshal.ReleaseComObject(service);
        }
        surfaces = result;
        Version++;
    }

    private Surface Render(string path, Rectangle bounds, int position)
    {
        int w = Math.Max(1, (bounds.Width + 3) / 4);
        int h = Math.Max(1, (bounds.Height + 3) / 4);
        int[] pixels = new int[w * h];
        using (Image photo = Image.FromFile(path))
        using (Bitmap reduced = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(reduced))
            {
                g.Clear(background);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.ScaleTransform((float)w / bounds.Width, (float)h / bounds.Height);
                Rectangle target = position == 5 ? SystemInformation.VirtualScreen : bounds;
                float width = photo.Width, height = photo.Height;
                if (position == 1)
                {
                    using (var texture = new TextureBrush(photo, WrapMode.Tile))
                        g.FillRectangle(texture, 0, 0, bounds.Width, bounds.Height);
                }
                else
                {
                    if (position == 2) { width = target.Width; height = target.Height; }
                    else if (position >= 3)
                    {
                        float sx = target.Width / width, sy = target.Height / height;
                        float scale = position == 3 ? Math.Min(sx, sy) : Math.Max(sx, sy);
                        width *= scale;
                        height *= scale;
                    }
                    float x = target.Left - bounds.Left + (target.Width - width) / 2f;
                    float y = target.Top - bounds.Top + (target.Height - height) / 2f;
                    g.DrawImage(photo, new RectangleF(x, y, width, height));
                }
            }
            BitmapData data = reduced.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { reduced.UnlockBits(data); }
        }

        // Three-pass separable binomial blur at quarter resolution for velvety frosted glass.
        int[] scratch = new int[pixels.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            Blur(pixels, scratch, w, h, true);
            Blur(scratch, pixels, w, h, false);
        }
        return new Surface { Bounds = bounds, Width = w, Height = h, Pixels = pixels };
    }

    private static void Blur(int[] source, int[] dest, int w, int h, bool horizontal)
    {
        int[] weights = { 1, 4, 6, 4, 1 };
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int k = -2; k <= 2; k++)
                {
                    int xx = horizontal ? Math.Max(0, Math.Min(w - 1, x + k)) : x;
                    int yy = horizontal ? y : Math.Max(0, Math.Min(h - 1, y + k));
                    int pixel = source[yy * w + xx];
                    int weight = weights[k + 2];
                    r += ((pixel >> 16) & 255) * weight;
                    g += ((pixel >> 8) & 255) * weight;
                    b += (pixel & 255) * weight;
                }
                dest[y * w + x] = unchecked((int)0xff000000) | ((r >> 4) << 16) | ((g >> 4) << 8) | (b >> 4);
            }
        }
    }

    public Color Sample(int screenX, int screenY)
    {
        foreach (Surface s in surfaces)
        {
            if (!s.Bounds.Contains(screenX, screenY)) continue;
            float fx = (screenX - s.Bounds.Left) * (float)(s.Width - 1) / Math.Max(1, s.Bounds.Width - 1);
            float fy = (screenY - s.Bounds.Top) * (float)(s.Height - 1) / Math.Max(1, s.Bounds.Height - 1);
            int x0 = Math.Max(0, Math.Min(s.Width - 1, (int)fx));
            int y0 = Math.Max(0, Math.Min(s.Height - 1, (int)fy));
            int x1 = Math.Min(s.Width - 1, x0 + 1);
            int y1 = Math.Min(s.Height - 1, y0 + 1);
            float tx = fx - x0;
            float ty = fy - y0;

            int c00 = s.Pixels[y0 * s.Width + x0];
            int c10 = s.Pixels[y0 * s.Width + x1];
            int c01 = s.Pixels[y1 * s.Width + x0];
            int c11 = s.Pixels[y1 * s.Width + x1];

            int r = Bilerp((c00 >> 16) & 255, (c10 >> 16) & 255, (c01 >> 16) & 255, (c11 >> 16) & 255, tx, ty);
            int g = Bilerp((c00 >> 8) & 255, (c10 >> 8) & 255, (c01 >> 8) & 255, (c11 >> 8) & 255, tx, ty);
            int b = Bilerp(c00 & 255, c10 & 255, c01 & 255, c11 & 255, tx, ty);
            return Color.FromArgb(255, r, g, b);
        }
        return background;
    }

    private static int Bilerp(int c00, int c10, int c01, int c11, float tx, float ty)
    {
        float top = c00 + (c10 - c00) * tx;
        float bottom = c01 + (c11 - c01) * tx;
        return Math.Max(0, Math.Min(255, (int)(top + (bottom - top) * ty + 0.5f)));
    }

    public void Dispose() { surfaces.Clear(); }
}
