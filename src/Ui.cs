using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Copyless
{
    static class Icons
    {
        static Bitmap symbol;

        /// <summary>The logo symbol, embedded from assets\symbol.png.</summary>
        static Bitmap Symbol
        {
            get
            {
                if (symbol == null)
                    using (Stream s = typeof(Icons).Assembly.GetManifestResourceStream("Copyless.symbol.png"))
                        symbol = new Bitmap(s);
                return symbol;
            }
        }

        static Graphics HighQuality(Bitmap b)
        {
            Graphics g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            return g;
        }

        /// <summary>Application icon: the symbol on a white rounded tile, readable on any background.</summary>
        public static Bitmap Tile(int size)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = HighQuality(b))
            {
                var rect = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
                using (var path = Rounded(rect, size * 0.22f))
                {
                    g.FillPath(Brushes.White, path);
                    if (size >= 24)
                        using (var pen = new Pen(Color.FromArgb(46, 0, 0, 0), Math.Max(1f, size / 128f))) g.DrawPath(pen, path);
                }
                float pad = size * 0.17f;
                g.DrawImage(Symbol, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad));
            }
            return b;
        }

        /// <summary>
        /// Notification area glyph. Dark strokes on a light taskbar, light strokes on a dark one;
        /// the blue accent turns grey while paused.
        /// </summary>
        public static Bitmap Glyph(int size, bool darkTaskbar, bool on)
        {
            const int work = 256;
            using (var big = new Bitmap(work, work, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = HighQuality(big)) g.DrawImage(Symbol, 0, 0, work, work);
                BitmapData data = big.LockBits(new Rectangle(0, 0, work, work), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                var px = new byte[data.Stride * work];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                for (int i = 0; i < px.Length; i += 4)
                {
                    if (px[i + 3] == 0) continue;
                    byte b = px[i], gr = px[i + 1], r = px[i + 2];
                    bool accent = b > r + 60 && b > gr + 30;
                    if (accent && !on) { px[i] = 152; px[i + 1] = 143; px[i + 2] = 138; }
                    else if (!accent && darkTaskbar) { px[i] = 243; px[i + 1] = 240; px[i + 2] = 240; }
                }
                System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
                big.UnlockBits(data);

                var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = HighQuality(result)) g.DrawImage(big, 0, 0, size, size);
                return result;
            }
        }

        /// <summary>True when the taskbar uses the dark theme (the Windows default).</summary>
        public static bool DarkTaskbar()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return !(v is int && (int)v == 1);
                }
            }
            catch { return true; }
        }

        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Icon Tray(bool on)
        {
            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap b = Glyph(s, DarkTaskbar(), on)) return Icon.FromHandle(b.GetHicon());
        }

        /// <summary>Writes a multi-resolution .ico file (PNG frames).</summary>
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = Tile(s))
                using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); frames.Add(ms.ToArray()); }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }
    }

    /// <summary>Topmost, click-through window that never takes focus.</summary>
    class GhostForm : Form
    {
        public GhostForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                return cp;
            }
        }
        public void ShowAt(Rectangle r)
        {
            if (!IsHandleCreated) CreateHandle();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            if (!Visible) { Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE); Visible = true; }
        }
    }

    /// <summary>Status bubble shown next to the cursor.</summary>
    class Toast : GhostForm
    {
        string title = "", body = "";
        bool warn;
        readonly Timer timer = new Timer { Interval = 15 };
        int shownAt;
        float scale = 1f;
        Font titleFont, bodyFont;

        public Toast()
        {
            BackColor = Color.FromArgb(28, 29, 33);
            timer.Tick += (s, e) => Tick();
        }

        public void Pop(string title, string body, bool warn)
        {
            this.title = title; this.body = body ?? ""; this.warn = warn;
            Point c = Cursor.Position;
            float sc = Native.ScaleAt(c.X, c.Y);
            if (titleFont == null || sc != scale)
            {
                scale = sc;
                titleFont = new Font("Segoe UI Semibold", 13f * scale, GraphicsUnit.Pixel);
                bodyFont = new Font("Segoe UI", 12f * scale, GraphicsUnit.Pixel);
            }
            int pad = (int)(10 * scale), icon = (int)(18 * scale);
            Size ts = TextRenderer.MeasureText(this.title, titleFont);
            Size bs = this.body.Length > 0 ? TextRenderer.MeasureText(this.body, bodyFont) : Size.Empty;
            int maxW = (int)(340 * scale);
            int w = Math.Min(maxW, pad * 3 + icon + Math.Max(ts.Width, bs.Width));
            int h = pad * 2 + ts.Height + (bs.Height > 0 ? bs.Height + (int)(2 * scale) : 0);
            h = Math.Max(h, pad * 2 + icon);

            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int x = c.X + (int)(18 * scale), y = c.Y + (int)(22 * scale);
            if (x + w > wa.Right) x = c.X - w - (int)(8 * scale);
            if (y + h > wa.Bottom) y = c.Y - h - (int)(8 * scale);
            x = Math.Max(wa.Left, x); y = Math.Max(wa.Top, y);

            using (var p = Icons.Rounded(new RectangleF(0, 0, w, h), 8 * scale)) Region = new Region(p);
            Opacity = 0.96;
            ShowAt(new Rectangle(x, y, w, h));
            Invalidate();
            shownAt = Environment.TickCount;
            timer.Start();
        }

        void Tick()
        {
            int age = Environment.TickCount - shownAt;
            int hold = warn ? 3200 : 1100;
            if (age < hold) return;
            double o = 0.96 - (age - hold) / 220.0;
            if (o <= 0) { timer.Stop(); Hide(); return; }
            Opacity = o;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = (int)(10 * scale), icon = (int)(18 * scale);
            using (var border = new Pen(Color.FromArgb(70, 255, 255, 255)))
            using (var p = Icons.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * scale))
                g.DrawPath(border, p);
            var ic = new RectangleF(pad, (Height - icon) / 2f, icon, icon);
            using (var br = new SolidBrush(warn ? Color.FromArgb(234, 160, 40) : Color.FromArgb(46, 160, 90))) g.FillEllipse(br, ic);
            using (var pen = new Pen(Color.White, 2f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (warn)
                {
                    g.DrawLine(pen, ic.X + icon / 2f, ic.Y + icon * 0.25f, ic.X + icon / 2f, ic.Y + icon * 0.58f);
                    g.DrawLine(pen, ic.X + icon / 2f, ic.Y + icon * 0.76f, ic.X + icon / 2f, ic.Y + icon * 0.77f);
                }
                else
                    g.DrawLines(pen, new[] {
                        new PointF(ic.X + icon * 0.27f, ic.Y + icon * 0.52f),
                        new PointF(ic.X + icon * 0.44f, ic.Y + icon * 0.68f),
                        new PointF(ic.X + icon * 0.74f, ic.Y + icon * 0.34f) });
            }
            int tx = pad * 2 + icon;
            Size ts = TextRenderer.MeasureText(title, titleFont);
            int tw = Width - tx - pad;
            int ty = pad;
            if (body.Length == 0) ty = (Height - ts.Height) / 2;
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(tx, ty, tw, ts.Height), Color.White,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (body.Length > 0)
                TextRenderer.DrawText(g, body, bodyFont, new Rectangle(tx, ty + ts.Height + (int)(2 * scale), tw, Height - ty - ts.Height),
                    Color.FromArgb(185, 188, 196), TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Selection rectangle for the live OCR drag.</summary>
    class OcrBox : GhostForm
    {
        public OcrBox()
        {
            BackColor = Color.FromArgb(40, 130, 255);
            Opacity = 0.30;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Color.White, 2)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    /// <summary>Full-screen region picker over a frozen screenshot.</summary>
    class SnipForm : Form
    {
        readonly Bitmap shot;
        readonly Rectangle virt;
        Point start;
        Rectangle sel;
        bool dragging;
        readonly Action<Bitmap, Rectangle> done;
        readonly Font hintFont = new Font("Segoe UI", 15f, GraphicsUnit.Pixel);

        public SnipForm(Bitmap shot, Rectangle virt, Action<Bitmap, Rectangle> done)
        {
            this.shot = shot; this.virt = virt; this.done = done;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Bounds = virt;
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Bounds = virt; Activate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(shot, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            using (var region = new Region(new Rectangle(0, 0, Width, Height)))
            {
                if (sel.Width > 0 && sel.Height > 0) region.Exclude(sel);
                g.FillRegion(dim, region);
            }
            if (sel.Width > 0 && sel.Height > 0)
                using (var pen = new Pen(Color.FromArgb(80, 160, 255), 2)) g.DrawRectangle(pen, sel);
            else
            {
                // hint on the monitor under the cursor
                Rectangle mon = Screen.FromPoint(Cursor.Position).Bounds;
                mon.Offset(-virt.X, -virt.Y);
                string hint = "Drag a box around the text to copy it. Press Esc to cancel.";
                Size s = TextRenderer.MeasureText(hint, hintFont);
                var r = new Rectangle(mon.X + (mon.Width - s.Width) / 2 - 16, mon.Y + 28, s.Width + 32, s.Height + 16);
                using (var bg = new SolidBrush(Color.FromArgb(220, 28, 29, 33))) g.FillRectangle(bg, r);
                TextRenderer.DrawText(g, hint, hintFont, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            dragging = true; start = e.Location; sel = Rectangle.Empty; Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            sel = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            Rectangle r = Rectangle.Intersect(sel, new Rectangle(0, 0, shot.Width, shot.Height));
            Bitmap crop = r.Width >= 4 && r.Height >= 4 ? shot.Clone(r, shot.PixelFormat) : null;
            Close();
            if (crop != null)
            {
                var screenRect = r; screenRect.Offset(virt.X, virt.Y);
                done(crop, screenRect);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            shot.Dispose();
        }
    }
}
