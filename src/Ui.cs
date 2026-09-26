using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

// Componentes visuales compartidos con Cursor Hub (estética de creador-de-flores).
namespace OptiHub
{
    static class Theme
    {
        // paleta del repo "creador-de-flores": papel crema, tinta oscura, trazos finos.
        // el modo oscuro sigue su escenario "noche": papel tostado y tinta crema
        public static Color Paper, PaperDark, Ink, Mid, Faint, Tile1, Tile2, ListBg;
        public static bool Dark { get; private set; }
        static Theme() { SetDark(false); }

        public static void SetDark(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Paper = Color.FromArgb(30, 24, 16); PaperDark = Color.FromArgb(46, 38, 27);
                Ink = Color.FromArgb(238, 230, 214); Mid = Color.FromArgb(180, 164, 134); Faint = Color.FromArgb(84, 72, 54);
                Tile1 = Color.FromArgb(16, 12, 7); Tile2 = Color.FromArgb(40, 32, 22); ListBg = Color.FromArgb(34, 28, 20);
            }
            else
            {
                Paper = Color.FromArgb(245, 240, 232); PaperDark = Color.FromArgb(232, 224, 208);
                Ink = Color.FromArgb(26, 18, 8); Mid = Color.FromArgb(90, 74, 48); Faint = Color.FromArgb(200, 191, 170);
                Tile1 = Color.FromArgb(52, 40, 26); Tile2 = Color.FromArgb(26, 18, 8); ListBg = Color.FromArgb(241, 236, 227);
            }
            if (noise != null) { noise.Dispose(); noise = null; }
        }

        // preferencia guardada: "dark", "light" o nada (sigue a Windows)
        static string SettingsFile { get { return Path.Combine(App.Root, "theme.txt"); } }
        public static bool LoadPreference()
        {
            try { if (File.Exists(SettingsFile)) return File.ReadAllText(SettingsFile).Trim() == "dark"; } catch { }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if (k != null) { var v = k.GetValue("AppsUseLightTheme"); if (v is int) return (int)v == 0; }
            }
            catch { }
            return false;
        }
        public static void SavePreference(bool dark)
        {
            try { Directory.CreateDirectory(App.Root); File.WriteAllText(SettingsFile, dark ? "dark" : "light"); } catch { }
        }

        // todo se diseña en píxeles lógicos (96 dpi) y se escala con K
        static float k;
        public static float K { get { if (k == 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f; return k; } }
        public static int S(float v) { return (int)Math.Round(v * K); }
        public static Padding S(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        static string serif, body;
        static string Pick(params string[] names)
        {
            var have = new InstalledFontCollection().Families.Select(f => f.Name).ToList();
            foreach (var n in names) if (have.Contains(n)) return n;
            return "Georgia";
        }
        public static Font Serif(float px, FontStyle st) { if (serif == null) serif = Pick("IM Fell English", "Palatino Linotype", "Book Antiqua", "Georgia"); return new Font(serif, px, st, GraphicsUnit.Pixel); }
        public static Font Body(float px, FontStyle st) { if (body == null) body = Pick("Cormorant Garamond", "EB Garamond", "Garamond", "Georgia"); return new Font(body, px, st, GraphicsUnit.Pixel); }

        static TextureBrush noise;
        public static void PaintPaper(Graphics g, Rectangle r)
        {
            if (noise == null)
            {
                var bmp = new Bitmap(160, 160); var rnd = new Random(7);
                for (int y = 0; y < 160; y++) for (int x = 0; x < 160; x++) bmp.SetPixel(x, y, Dark ? Color.FromArgb(rnd.Next(0, 9), 255, 236, 200) : Color.FromArgb(rnd.Next(0, 12), 70, 50, 20));
                noise = new TextureBrush(bmp);
            }
            using (var b = new SolidBrush(Paper)) g.FillRectangle(b, r);
            g.FillRectangle(noise, r);
        }

        // prepara el Graphics para dibujar en píxeles lógicos
        public static void Begin(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(K, K);
        }

        public static float SpacedWidth(Graphics g, string t, Font f, float sp)
        {
            float w = 0;
            foreach (char c in t) w += (c == ' ' ? f.Size * 0.32f : g.MeasureString(c.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width) + sp;
            return t.Length > 0 ? w - sp : 0;
        }
        public static void Spaced(Graphics g, string t, Font f, Color col, float x, float y, float sp)
        {
            using (var b = new SolidBrush(col))
                foreach (char c in t)
                {
                    if (c == ' ') { x += f.Size * 0.32f + sp; continue; }
                    g.DrawString(c.ToString(), f, b, x, y, StringFormat.GenericTypographic);
                    x += g.MeasureString(c.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width + sp;
                }
        }
        public static void Text(Graphics g, string t, Font f, Color col, RectangleF r, bool wrap)
        {
            var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
            if (!wrap) sf.FormatFlags |= StringFormatFlags.NoWrap;
            using (var b = new SolidBrush(col)) g.DrawString(t, f, b, r, sf);
        }
    }

    class PaperPanel : Panel
    {
        public PaperPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e) { Theme.PaintPaper(e.Graphics, ClientRectangle); }
    }

    class PaperFlow : FlowLayoutPanel
    {
        public PaperFlow() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e) { Theme.PaintPaper(e.Graphics, ClientRectangle); }
    }

    // botón de contorno que se rellena de tinta desde abajo al pasar el cursor
    class InkButton : Control
    {
        public bool Primary; float t; bool hover; Timer tm = new Timer { Interval = 15 };
        const float Px = 13.5f, Sp = 1.8f;

        public static int WidthFor(string text, int pad)
        {
            using (var bmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bmp)) using (var f = Theme.Body(Px, FontStyle.Regular))
                return (int)Math.Ceiling(Theme.SpacedWidth(g, text.ToUpper(), f, Sp)) + pad * 2;
        }

        public InkButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            tm.Tick += delegate
            {
                float target = hover && Enabled ? 1f : 0f; t += (target - t) * 0.4f;
                if (Math.Abs(target - t) < 0.03f) { t = target; tm.Stop(); }
                Invalidate();
            };
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; tm.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; tm.Start(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.PaintPaper(g, ClientRectangle); Theme.Begin(g);
            float W = Width / Theme.K, H = Height / Theme.K;
            var r = new RectangleF(0.5f, 0.5f, W - 1.5f, H - 1.5f);
            if (Primary && Enabled) using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, r);
            float h = H * t;
            if (h > 0.5f) using (var b = new SolidBrush(Primary ? Theme.Mid : Theme.Ink)) g.FillRectangle(b, 0.5f, H - h, W - 1.5f, h);
            using (var pen = new Pen(Enabled ? Theme.Ink : Theme.Faint, 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            Color tc = !Enabled ? Theme.Faint : Primary ? Theme.Paper : (t > 0.5f ? Theme.Paper : Theme.Ink);
            using (var f = Theme.Body(Px, FontStyle.Regular))
            {
                string s = Text.ToUpper(); float sp = Sp, w = Theme.SpacedWidth(g, s, f, sp);
                if (w > W - 12 && s.Length > 1) { sp = Math.Max(0, sp - (w - (W - 12)) / (s.Length - 1)); w = Theme.SpacedWidth(g, s, f, sp); }
                Theme.Spaced(g, s, f, tc, (W - w) / 2, (H - f.GetHeight(g)) / 2 + 1, sp);
            }
        }
    }

    class SectionHeader : Control
    {
        public SectionHeader() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Height = Theme.S(32); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.PaintPaper(g, ClientRectangle); Theme.Begin(g);
            float W = Width / Theme.K;
            using (var f = Theme.Body(12.5f, FontStyle.Regular)) Theme.Spaced(g, Text.ToUpper(), f, Theme.Mid, 1, 7, 3f);
            using (var pen = new Pen(Theme.Faint, 1f)) g.DrawLine(pen, 0, 27, W - 4, 27);
        }
    }

    class WindowButtons : Control
    {
        public event EventHandler CloseClick, MinClick, MaxClick;
        bool hover;
        public WindowButtons() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Size = new Size(Theme.S(74), Theme.S(20)); }
        int Hit(Point p) { float x = p.X / Theme.K; for (int i = 0; i < 3; i++) if (x >= i * 24 && x < i * 24 + 20) return i; return -1; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h == 0 && CloseClick != null) CloseClick(this, EventArgs.Empty);
            if (h == 1 && MinClick != null) MinClick(this, EventArgs.Empty);
            if (h == 2 && MaxClick != null) MaxClick(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.PaintPaper(g, ClientRectangle); Theme.Begin(g);
            for (int i = 0; i < 3; i++)
            {
                var r = new RectangleF(i * 24 + 2, 3, 13, 13);
                using (var pen = new Pen(Theme.Ink, 1f))
                {
                    g.DrawEllipse(pen, r);
                    if (!hover) continue;
                    float cx = r.X + 6.5f, cy = r.Y + 6.5f;
                    if (i == 0) { g.DrawLine(pen, cx - 3, cy - 3, cx + 3, cy + 3); g.DrawLine(pen, cx + 3, cy - 3, cx - 3, cy + 3); }
                    else { g.DrawLine(pen, cx - 3.5f, cy, cx + 3.5f, cy); if (i == 2) g.DrawLine(pen, cx, cy - 3.5f, cx, cy + 3.5f); }
                }
            }
        }
    }

    // luna / sol dibujados a trazo para cambiar de modo
    class ThemeToggle : Control
    {
        bool hover;
        public ThemeToggle() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); Size = new Size(Theme.S(24), Theme.S(24)); Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.PaintPaper(g, ClientRectangle); Theme.Begin(g);
            using (var pen = new Pen(hover ? Theme.Ink : Theme.Mid, hover ? 1.4f : 1.1f))
            {
                if (Theme.Dark) // sol: pasa a modo claro
                {
                    g.DrawEllipse(pen, 8, 8, 8, 8);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4; float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                        g.DrawLine(pen, 12 + c * 6.5f, 12 + s * 6.5f, 12 + c * 9.5f, 12 + s * 9.5f);
                    }
                }
                else // luna: pasa a modo oscuro
                {
                    // media luna: círculo exterior sin la parte que tapa el interior, y viceversa
                    using (var outer = new GraphicsPath()) using (var inner = new GraphicsPath())
                    {
                        outer.AddEllipse(5, 5, 14, 14); inner.AddEllipse(9.5f, 2.5f, 12, 12);
                        var st = g.Save();
                        g.SetClip(inner, CombineMode.Exclude); g.DrawEllipse(pen, 5, 5, 14, 14); g.Restore(st);
                        st = g.Save();
                        g.SetClip(outer, CombineMode.Intersect); g.DrawEllipse(pen, 9.5f, 2.5f, 12, 12); g.Restore(st);
                    }
                }
            }
        }
    }

    class InkMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(Theme.Paper)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var p = new Pen(Theme.Faint)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected) using (var b = new SolidBrush(Theme.PaperDark)) e.Graphics.FillRectangle(b, 2, 0, e.Item.Width - 4, e.Item.Height);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Ink; base.OnRenderItemText(e); }
    }

}
