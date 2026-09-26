using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Opti Hub")]
[assembly: AssemblyDescription("Optimiza Windows 11 según tu hardware: gaming, general u ofimática.")]
[assembly: AssemblyProduct("Opti Hub")]
[assembly: AssemblyCompany("kisnner26")]
[assembly: AssemblyCopyright("© 2026 kisnner26 · github.com/kisnner26")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace OptiHub
{
    enum View { Equipo, Optimizar, Procesos, Inicio, Herramientas }

    // Fila de la lista: casilla opcional, título, texto, etiqueta y una acción a la derecha.
    class RowCard : Control
    {
        public const int LH = 78;
        public string Title = "", Body = "", Badge = "", Action = "";
        public int BadgeStyle; // 1 tinta, 2 contorno, 3 tenue
        public bool HasCheck, Checked, Locked;
        public event EventHandler Toggled, ActionClick;
        RectangleF actRect; bool hover, hoverAct;

        public RowCard() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true); Height = Theme.S(LH); }
        PointF L(Point p) { return new PointF(p.X / Theme.K, p.Y / Theme.K); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; hoverAct = false; Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool a = Action.Length > 0 && actRect.Contains(L(e.Location));
            Cursor = a || (HasCheck && !Locked) ? Cursors.Hand : Cursors.Default;
            if (a != hoverAct) { hoverAct = a; Invalidate(); }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (Action.Length > 0 && actRect.Contains(L(e.Location))) { if (ActionClick != null) ActionClick(this, EventArgs.Empty); }
            else if (HasCheck && !Locked) { Checked = !Checked; Invalidate(); if (Toggled != null) Toggled(this, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.PaintPaper(g, ClientRectangle); Theme.Begin(g);
            float W = Width / Theme.K, H = LH;
            var r = new RectangleF(0.5f, 0.5f, W - 1.5f, H - 1.5f);
            if (hover) using (var b = new SolidBrush(Color.FromArgb(70, Theme.PaperDark))) g.FillRectangle(b, r);
            using (var pen = new Pen(hover ? Theme.Ink : Theme.Faint, 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            float x = 18;
            if (HasCheck)
            {
                var box = new RectangleF(18, H / 2 - 9, 18, 18);
                if (Checked) using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, box);
                using (var pen = new Pen(Locked ? Theme.Faint : Theme.Ink, 1f)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                if (Checked) using (var pen = new Pen(Theme.Paper, 1.8f))
                        g.DrawLines(pen, new[] { new PointF(box.X + 4, box.Y + 9.5f), new PointF(box.X + 7.5f, box.Y + 13), new PointF(box.X + 14, box.Y + 5) });
                x = 52;
            }

            float right = Badge.Length == 0 && Action.Length == 0 ? 18 : 170;
            if (Badge.Length > 0)
                using (var f = Theme.Body(11.5f, FontStyle.Regular))
                {
                    string t = Badge.ToUpper(); float tw = Theme.SpacedWidth(g, t, f, 1.5f);
                    var tr = new RectangleF(W - 16 - tw - 16, 12, tw + 16, 21);
                    if (BadgeStyle == 1) using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, tr);
                    else using (var pen = new Pen(BadgeStyle == 3 ? Theme.Faint : Theme.Ink, 1f)) g.DrawRectangle(pen, tr.X, tr.Y, tr.Width, tr.Height);
                    Theme.Spaced(g, t, f, BadgeStyle == 1 ? Theme.Paper : BadgeStyle == 3 ? Theme.Mid : Theme.Ink, tr.X + 8, tr.Y + 4, 1.5f);
                }
            if (Action.Length > 0)
                using (var f = Theme.Body(15f, FontStyle.Italic))
                {
                    float aw = g.MeasureString(Action, f).Width;
                    actRect = new RectangleF(W - 16 - aw, H - 32, aw, 24);
                    using (var b = new SolidBrush(hoverAct ? Theme.Ink : Theme.Mid)) g.DrawString(Action, f, b, actRect.X, actRect.Y);
                    if (hoverAct) using (var pen = new Pen(Theme.Ink, 1f)) g.DrawLine(pen, actRect.X + 3, actRect.Bottom - 2, actRect.Right - 4, actRect.Bottom - 2);
                }

            using (var f = Theme.Serif(16f, FontStyle.Regular))
                Theme.Text(g, Title, f, Locked ? Theme.Mid : Theme.Ink, new RectangleF(x, 11, W - x - right, 24), false);
            using (var f = Theme.Body(14f, FontStyle.Italic))
                Theme.Text(g, Body, f, Theme.Mid, new RectangleF(x, 35, W - x - right, 38), true);
        }
    }

    // Selector de perfil: Gaming / General / Ofimática.
    class ProfileBar : Control
    {
        public event Action<Profile> Picked;
        readonly string hint;
        public ProfileBar(Profile current, Profile suggested)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = Theme.S(58);
            hint = "sugerido para tu equipo: " + MainForm.ProfileName(suggested).ToLower();
            for (int i = 0; i < 3; i++)
            {
                var p = (Profile)i;
                var b = new InkButton { Text = MainForm.ProfileName(p), Primary = p == current };
                b.SetBounds(Theme.S(i * 158), Theme.S(6), Theme.S(148), Theme.S(40));
                b.Click += delegate { if (Picked != null) Picked(p); };
                Controls.Add(b);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.PaintPaper(g, ClientRectangle); Theme.Begin(g);
            using (var f = Theme.Body(15f, FontStyle.Italic)) Theme.Text(g, hint, f, Theme.Mid, new RectangleF(3 * 158 + 8, 16, Width / Theme.K - 3 * 158 - 8, 24), false);
        }
    }

    class MainForm : Form
    {
        ListBox nav = new ListBox();
        PaperFlow grid = new PaperFlow();
        PaperPanel head, foot;
        InkButton primary = new InkButton(), secondary = new InkButton();
        ThemeToggle toggle = new ThemeToggle();
        string status = "", title = "Opti Hub", sub = "";
        const string Credit = "creado por @kisnner26", CreditUrl = "https://github.com/kisnner26";
        RectangleF creditRect; bool creditHover;

        View view = View.Equipo;
        Hw hw;
        List<Tweak> tweaks = new List<Tweak>();
        Dictionary<string, bool> checks = new Dictionary<string, bool>();
        Profile profile = Profile.General;
        bool busy;

        static readonly string[] NavNames = { "Tu equipo", "Optimizar", "Procesos", "Inicio de Windows", "Herramientas" };

        // procesos que nunca se ofrecen para cerrar
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso", "svchost", "dwm",
            "fontdrvhost", "sihost", "ctfmon", "audiodg", "spoolsv", "MsMpEng", "NisSrv", "SecurityHealthService", "SgrmBroker",
            "Memory Compression", "Secure System", "WUDFHost", "conhost", "explorer", "StartMenuExperienceHost", "ShellExperienceHost",
            "SearchHost", "TextInputHost", "RuntimeBroker", "dllhost", "taskhostw", "smartscreen", "WmiPrvSE", "MpDefenderCoreService",
            "ApplicationFrameHost", "LockApp", "SearchIndexer", "OptiHub", "vmmem", "vmcompute", "wlanext", "unsecapp", "SystemSettings" };

        public static string ProfileName(Profile p) { return p == Profile.Gaming ? "Gaming" : p == Profile.General ? "General" : "Ofimática"; }
        static int S(float v) { return Theme.S(v); }
        static string Gb(double g) { return Math.Round(g, g < 10 ? 1 : 0) + " GB"; }
        static string Mb(long bytes) { return bytes >= 1073741824 ? Gb(bytes / 1073741824.0) : (bytes / 1048576) + " MB"; }
        static bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks == 2) { ToggleMax(); return; }
            ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0);
        }
        void ToggleMax() { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal) // WM_NCHITTEST: redimensionar desde los bordes
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = S(7); bool l = p.X < g, r = p.X >= Width - g, t = p.Y < g, b = p.Y >= Height - g;
                if (t && l) { m.Result = (IntPtr)13; return; }
                if (t && r) { m.Result = (IntPtr)14; return; }
                if (b && l) { m.Result = (IntPtr)16; return; }
                if (b && r) { m.Result = (IntPtr)17; return; }
                if (l) { m.Result = (IntPtr)10; return; }
                if (r) { m.Result = (IntPtr)11; return; }
                if (t) { m.Result = (IntPtr)12; return; }
                if (b) { m.Result = (IntPtr)15; return; }
            }
            base.WndProc(ref m);
        }

        void ApplyTheme()
        {
            BackColor = Theme.Mid; nav.BackColor = Theme.ListBg;
            try { SetWindowTheme(grid.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null); SetWindowTheme(nav.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
            Invalidate(true);
        }

        public MainForm()
        {
            FormBorderStyle = FormBorderStyle.None; Padding = new Padding(1);
            Text = "Opti Hub"; ClientSize = new Size(S(1160), S(720)); MinimumSize = new Size(S(940), S(600));
            StartPosition = FormStartPosition.CenterScreen; Theme.SetDark(Theme.LoadPreference()); BackColor = Theme.Mid; DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // ---- barra lateral ----
            var side = new PaperPanel { Dock = DockStyle.Left, Width = S(262), Padding = Theme.S(18, 0, 18, 16) };
            side.Paint += delegate (object o, PaintEventArgs e) { using (var pen = new Pen(Theme.Faint)) e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height); };

            var chrome = new PaperPanel { Dock = DockStyle.Top, Height = S(120) };
            var wb = new WindowButtons { Left = 0, Top = S(16) };
            wb.CloseClick += delegate { Close(); }; wb.MinClick += delegate { WindowState = FormWindowState.Minimized; }; wb.MaxClick += delegate { ToggleMax(); };
            toggle.Left = S(202); toggle.Top = S(14);
            toggle.Click += delegate { Theme.SetDark(!Theme.Dark); Theme.SavePreference(Theme.Dark); ApplyTheme(); Say(Theme.Dark ? "modo noche." : "modo día."); };
            new ToolTip().SetToolTip(toggle, "Cambiar modo día / noche");
            chrome.Controls.Add(wb); chrome.Controls.Add(toggle); chrome.MouseDown += Drag;
            chrome.Paint += delegate (object o, PaintEventArgs e)
            {
                var g = e.Graphics; Theme.Begin(g); float w = chrome.Width / Theme.K;
                using (var f = Theme.Serif(27f, FontStyle.Regular)) Theme.Text(g, "Opti Hub", f, Theme.Ink, new RectangleF(0, 44, w, 40), false);
                using (var f = Theme.Body(15f, FontStyle.Italic)) Theme.Text(g, "un cuaderno para tu pc", f, Theme.Mid, new RectangleF(2, 84, w, 24), false);
            };

            var hint = new PaperPanel { Dock = DockStyle.Top, Height = S(34) };
            hint.Paint += delegate (object o, PaintEventArgs e)
            {
                var g = e.Graphics; Theme.Begin(g);
                using (var f = Theme.Body(12.5f, FontStyle.Regular)) Theme.Spaced(g, "SECCIONES", f, Theme.Mid, 1, 8, 3f);
                using (var pen = new Pen(Theme.Faint)) g.DrawLine(pen, 0, 29, hint.Width / Theme.K, 29);
            };

            nav.Dock = DockStyle.Fill; nav.BorderStyle = BorderStyle.None; nav.BackColor = Theme.ListBg; nav.IntegralHeight = false;
            nav.DrawMode = DrawMode.OwnerDrawFixed; nav.ItemHeight = S(56);
            foreach (var n in NavNames) nav.Items.Add(n);
            nav.DrawItem += DrawNavItem;
            nav.SelectedIndexChanged += delegate { if (nav.SelectedIndex >= 0 && (View)nav.SelectedIndex != view) { view = (View)nav.SelectedIndex; RefreshView(); } };
            var lpad = new PaperPanel { Dock = DockStyle.Fill, Padding = Theme.S(0, 10, 0, 0) }; lpad.Controls.Add(nav);

            var note = new PaperPanel { Dock = DockStyle.Bottom, Height = S(96) };
            note.Paint += delegate (object o, PaintEventArgs e)
            {
                var g = e.Graphics; Theme.Begin(g); float w = note.Width / Theme.K;
                using (var pen = new Pen(Theme.Faint)) g.DrawLine(pen, 0, 8, w, 8);
                using (var f = Theme.Body(14f, FontStyle.Italic))
                    Theme.Text(g, "antes de aplicar se crea un punto de restauración, y cada ajuste guarda su valor anterior para deshacerlo cuando quieras.", f, Theme.Mid, new RectangleF(0, 18, w, 76), true);
            };
            side.Controls.Add(lpad); side.Controls.Add(hint); side.Controls.Add(chrome); side.Controls.Add(note);

            // ---- cabecera ----
            head = new PaperPanel { Dock = DockStyle.Top, Height = S(130) };
            primary.Primary = true; primary.SetBounds(0, S(38), S(150), S(40)); secondary.SetBounds(0, S(38), S(150), S(40));
            head.Controls.AddRange(new Control[] { primary, secondary });
            head.Paint += PaintHead; head.Resize += delegate { LayoutHead(); }; head.MouseDown += Drag;
            primary.Click += delegate { if (!busy && view == View.Optimizar) ApplySelected(); };
            secondary.Click += delegate { OnSecondary(); };

            // ---- pie ----
            foot = new PaperPanel { Dock = DockStyle.Bottom, Height = S(42) };
            foot.Paint += delegate (object o, PaintEventArgs e)
            {
                var g = e.Graphics; Theme.Begin(g); float w = foot.Width / Theme.K;
                using (var pen = new Pen(Theme.Faint)) g.DrawLine(pen, 0, 0.5f, w, 0.5f);
                using (var f = Theme.Body(15f, FontStyle.Italic))
                {
                    float cw = g.MeasureString(Credit, f).Width;
                    creditRect = new RectangleF(w - 34 - cw, 9, cw, 24);
                    Theme.Text(g, status, f, Theme.Mid, new RectangleF(34, 11, creditRect.X - 60, 24), false);
                    using (var b = new SolidBrush(creditHover ? Theme.Ink : Theme.Mid)) g.DrawString(Credit, f, b, creditRect.X, 11);
                    if (creditHover) using (var pen = new Pen(Theme.Ink, 1f)) g.DrawLine(pen, creditRect.X + 3, 31, creditRect.Right - 5, 31);
                }
            };
            foot.MouseMove += delegate (object o, MouseEventArgs e)
            {
                bool h = creditRect.Contains(e.X / Theme.K, e.Y / Theme.K);
                if (h != creditHover) { creditHover = h; foot.Cursor = h ? Cursors.Hand : Cursors.Default; foot.Invalidate(); }
            };
            foot.MouseLeave += delegate { if (creditHover) { creditHover = false; foot.Cursor = Cursors.Default; foot.Invalidate(); } };
            foot.MouseClick += delegate (object o, MouseEventArgs e) { if (creditRect.Contains(e.X / Theme.K, e.Y / Theme.K)) Open(CreditUrl, ""); };

            // ---- contenido ----
            grid.Dock = DockStyle.Fill; grid.AutoScroll = true; grid.Padding = Theme.S(30, 0, 0, 16);
            grid.Resize += delegate { int w = ContentWidth(); foreach (Control c in grid.Controls) c.Width = w; };
            Controls.Add(grid); Controls.Add(head); Controls.Add(foot); Controls.Add(side);
            Load += delegate { ApplyTheme(); nav.SelectedIndex = 0; Detect(); };
        }

        // ---------- estado general ----------

        void Say(string s) { status = s; foot.Invalidate(); }
        void Busy(bool b) { busy = b; primary.Enabled = !b; secondary.Enabled = !b; UseWaitCursor = b; }
        int ContentWidth() { return Math.Max(S(300), grid.ClientSize.Width - grid.Padding.Horizontal - S(10)); }
        void Open(string file, string args) { try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); } catch (Exception e) { Say("no se pudo abrir: " + e.Message); } }

        void Background(Action work, Action done)
        {
            new Thread(() =>
            {
                try { work(); }
                finally { BeginInvoke(done); }
            }) { IsBackground = true }.Start();
        }

        void Detect()
        {
            Busy(true); Say("reconociendo tu equipo…"); title = "Tu equipo"; sub = "un momento…"; head.Invalidate();
            Hw h = null; string err = null;
            Background(() => { try { h = Hw.Detect(); } catch (Exception e) { err = e.Message; } }, () =>
            {
                Busy(false);
                if (h == null) { Say("no se pudo reconocer el equipo: " + err); return; }
                hw = h; tweaks = Tweaks.Build(hw); PickProfile(Suggested());
                view = View.Equipo; nav.SelectedIndex = 0; RefreshView();
                Say("listo: " + hw.TierLabel + ".");
            });
        }

        Profile Suggested() { return hw != null && hw.DedicatedGpu && hw.Tier != "básico" ? Profile.Gaming : Profile.General; }

        void PickProfile(Profile p)
        {
            profile = p; checks.Clear();
            foreach (var t in tweaks) checks[t.Id] = t.Applies(hw) && t.Recommended(p, hw) && !Safe(t.IsApplied);
        }

        // ---------- cabecera ----------

        void SetButtons(string p, string s)
        {
            primary.Visible = p != null; secondary.Visible = s != null;
            if (p != null) { primary.Text = p; primary.Width = S(InkButton.WidthFor(p, 20)); }
            if (s != null) { secondary.Text = s; secondary.Width = S(InkButton.WidthFor(s, 20)); }
            primary.Enabled = secondary.Enabled = !busy;
            LayoutHead();
        }

        void LayoutHead()
        {
            int right = head.Width - S(34);
            if (primary.Visible) { primary.Left = right - primary.Width; right = primary.Left - S(12); }
            if (secondary.Visible) secondary.Left = right - secondary.Width;
            head.Invalidate();
        }

        void PaintHead(object o, PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Begin(g);
            int left = head.Width;
            if (primary.Visible) left = Math.Min(left, primary.Left);
            if (secondary.Visible) left = Math.Min(left, secondary.Left);
            float avail = left / Theme.K - 34 - 20;
            using (var f = Theme.Serif(38f, FontStyle.Regular)) Theme.Text(g, title, f, Theme.Ink, new RectangleF(32, 22, avail, 56), false);
            using (var f = Theme.Body(17f, FontStyle.Italic)) Theme.Text(g, sub, f, Theme.Mid, new RectangleF(35, 80, avail, 26), false);
            using (var pen = new Pen(Color.FromArgb(90, Theme.Ink))) g.DrawLine(pen, 36, 114, 126, 114);
        }

        void OnSecondary()
        {
            if (busy) return;
            switch (view)
            {
                case View.Equipo: Detect(); break;
                case View.Optimizar: RevertAll(); break;
                default: RefreshView(); break;
            }
        }

        // ---------- navegación ----------

        void DrawNavItem(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            using (var bg = new SolidBrush(nav.BackColor)) g.FillRectangle(bg, e.Bounds);
            var st = g.Save();
            g.TranslateTransform(e.Bounds.X, e.Bounds.Y); Theme.Begin(g);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            float w = e.Bounds.Width / Theme.K;
            var r = new RectangleF(0.5f, 3, w - 1.5f, 48);
            if (sel) using (var b = new SolidBrush(Theme.Ink)) g.FillRectangle(b, r);
            using (var pen = new Pen(sel ? Theme.Ink : Theme.Faint, 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            string[] subs = {
                hw == null ? "reconociendo…" : hw.TierLabel,
                "perfil " + ProfileName(profile).ToLower(),
                "cierra lo que sobra",
                "apps al encender",
                "limpieza y restauración" };
            using (var f = Theme.Serif(15f, FontStyle.Regular)) Theme.Text(g, NavNames[e.Index], f, sel ? Theme.Paper : Theme.Ink, new RectangleF(12, 8, w - 24, 22), false);
            using (var f = Theme.Body(13.5f, FontStyle.Italic)) Theme.Text(g, subs[e.Index], f, sel ? Theme.Faint : Theme.Mid, new RectangleF(12, 29, w - 24, 20), false);
            g.Restore(st);
        }

        void RefreshView()
        {
            grid.SuspendLayout();
            foreach (var c in grid.Controls.Cast<Control>().ToList()) c.Dispose();
            if (hw != null)
                switch (view)
                {
                    case View.Equipo: BuildEquipo(); break;
                    case View.Optimizar: BuildOptimizar(); break;
                    case View.Procesos: BuildProcesos(); break;
                    case View.Inicio: BuildInicio(); break;
                    case View.Herramientas: BuildHerramientas(); break;
                }
            grid.ResumeLayout(); grid.AutoScrollPosition = new Point(0, 0);
            LayoutHead(); nav.Invalidate();
        }

        RowCard Row(string t, string body)
        {
            var r = new RowCard { Title = t, Body = body, Width = ContentWidth(), Margin = Theme.S(4, 4, 4, 4) };
            grid.Controls.Add(r); return r;
        }
        void Section(string text) { grid.Controls.Add(new SectionHeader { Text = text, Width = ContentWidth(), Margin = Theme.S(4, 12, 4, 4) }); }

        // ---------- tu equipo ----------

        void BuildEquipo()
        {
            title = "Tu equipo"; sub = "reconocido automáticamente · " + hw.TierLabel;
            SetButtons(null, "Volver a detectar");
            Section("Hardware");
            Row("Procesador", hw.Cpu + "\n" + hw.Cores + " núcleos · " + hw.Threads + " hilos");
            Row("Memoria RAM", Gb(hw.RamGB) + " instalados" + (hw.RamGB < 8.5 ? " · poca memoria: conviene aligerar Windows" : hw.RamGB >= 15.5 ? " · memoria de sobra" : ""));
            Row("Tarjeta gráfica", hw.Gpu + "\n" + (hw.VramGB > 0.1 ? Gb(hw.VramGB) + " de memoria de vídeo · " : "") + (hw.DedicatedGpu ? "dedicada" : "integrada"));
            Row("Disco del sistema", hw.Disk + "\n" + hw.DiskKind + (hw.DiskGB > 0 ? " · " + Math.Round(hw.DiskGB) + " GB" : ""));
            Row("Tipo de equipo", hw.Laptop ? "Portátil\nse evitan los ajustes que gastan batería o calientan el equipo." : "Sobremesa\nse pueden usar planes de energía de máximo rendimiento.");
            Row("Sistema", hw.Windows + "\ncompilación " + hw.Build);

            Section("Recomendación");
            string why = hw.Tier == "básico" ? "Equipo básico: se priorizan ajustes ligeros (efectos visuales, servicios y apps de fondo) para liberar memoria y CPU."
                : hw.Tier == "alto" ? "Equipo potente: se prioriza la latencia y el rendimiento en juegos, manteniendo los efectos visuales."
                : "Equipo medio: se ajustan servicios, privacidad y prioridades sin perder funciones.";
            var r = Row("Perfil sugerido: " + ProfileName(Suggested()), why);
            r.Action = "ir a optimizar →";
            r.ActionClick += delegate { PickProfile(Suggested()); view = View.Optimizar; nav.SelectedIndex = (int)view; RefreshView(); };
        }

        // ---------- optimizar ----------

        void UpdateSub()
        {
            sub = "perfil " + ProfileName(profile).ToLower() + " · " + checks.Count(kv => kv.Value) + " ajustes seleccionados";
            head.Invalidate();
        }

        void BuildOptimizar()
        {
            title = "Optimizar"; UpdateSub();
            SetButtons("Aplicar selección", "Deshacer todo");
            var bar = new ProfileBar(profile, Suggested()) { Width = ContentWidth(), Margin = Theme.S(4, 0, 4, 0) };
            bar.Picked += p => { PickProfile(p); RefreshView(); };
            grid.Controls.Add(bar);

            foreach (var group in tweaks.GroupBy(t => t.Group))
            {
                Section(group.Key);
                foreach (var t in group)
                {
                    bool applies = t.Applies(hw), applied = applies && Safe(t.IsApplied);
                    var r = Row(t.Name, applies ? t.Desc + (t.NeedsReboot && !applied ? " Requiere reiniciar." : "") : t.WhyNot);
                    r.HasCheck = true; r.Locked = !applies; r.Checked = checks[t.Id];
                    if (!applies) { r.Badge = "no aplica"; r.BadgeStyle = 3; }
                    else if (applied) { r.Badge = "aplicado"; r.BadgeStyle = 1; }
                    else if (t.Recommended(profile, hw)) { r.Badge = "recomendado"; r.BadgeStyle = 2; }
                    if (t.HasBackup) { r.Action = "↺ deshacer"; r.ActionClick += delegate { RevertOne(t); }; }
                    r.Toggled += delegate { checks[t.Id] = r.Checked; UpdateSub(); };
                }
            }
        }

        void ApplySelected()
        {
            var sel = tweaks.Where(t => checks[t.Id]).ToList();
            if (sel.Count == 0) { Say("no hay ningún ajuste seleccionado."); return; }
            if (MessageBox.Show("Se aplicarán " + sel.Count + " ajustes del perfil " + ProfileName(profile) + ".\n\nAntes se creará un punto de restauración de Windows, y cada ajuste guarda su valor anterior para poder deshacerlo.\n\n¿Continuar?",
                "Opti Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            Busy(true); Say("creando punto de restauración… (puede tardar un poco)");
            string rpErr = null;
            Background(() => { rpErr = App.CreateRestorePoint(); }, () =>
            {
                if (rpErr != null && MessageBox.Show("No se pudo crear el punto de restauración (" + rpErr + ").\n\nCada ajuste sigue guardando su valor anterior y se puede deshacer desde Opti Hub. ¿Continuar igualmente?",
                    "Opti Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { Busy(false); Say("cancelado: no se aplicó nada."); return; }

                Say("aplicando ajustes…");
                var errors = new List<string>(); bool reboot = false;
                Background(() =>
                {
                    foreach (var t in sel)
                        try { t.Apply(); if (t.NeedsReboot) reboot = true; }
                        catch (Exception e) { errors.Add(t.Name + ": " + e.Message); }
                }, () =>
                {
                    Busy(false);
                    foreach (var t in sel) checks[t.Id] = false;
                    RefreshView();
                    string msg = (sel.Count - errors.Count) + " ajustes aplicados." + (reboot ? " Reinicia el equipo para completar algunos cambios." : "");
                    Say(msg);
                    if (errors.Count > 0) MessageBox.Show("Algunos ajustes no se aplicaron:\n\n" + string.Join("\n", errors.ToArray()), "Opti Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            });
        }

        void RevertOne(Tweak t)
        {
            if (busy) return;
            try { t.Revert(); Say("deshecho: " + t.Name.ToLower() + "."); }
            catch (Exception e) { MessageBox.Show("No se pudo deshacer: " + e.Message, "Opti Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            RefreshView();
        }

        void RevertAll()
        {
            var list = tweaks.Where(t => t.HasBackup).ToList();
            if (list.Count == 0) { Say("no hay ajustes de Opti Hub que deshacer."); return; }
            if (MessageBox.Show("¿Deshacer los " + list.Count + " ajustes aplicados con Opti Hub y volver a los valores anteriores?", "Opti Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Busy(true); Say("deshaciendo ajustes…");
            var errors = new List<string>();
            Background(() =>
            {
                foreach (var t in list)
                    try { t.Revert(); } catch (Exception e) { errors.Add(t.Name + ": " + e.Message); }
            }, () =>
            {
                Busy(false); RefreshView();
                Say((list.Count - errors.Count) + " ajustes deshechos. Reinicia para completar algunos cambios.");
                if (errors.Count > 0) MessageBox.Show("Algunos ajustes no se pudieron deshacer:\n\n" + string.Join("\n", errors.ToArray()), "Opti Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            });
        }

        // ---------- procesos ----------

        void BuildProcesos()
        {
            var procs = Process.GetProcesses();
            var groups = procs.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Name = g.Key, Count = g.Count(), Mem = g.Sum(p => { try { return p.WorkingSet64; } catch { return 0L; } }) })
                .OrderByDescending(g => g.Mem).ToList();
            long total = groups.Sum(g => g.Mem);
            foreach (var p in procs) p.Dispose();

            title = "Procesos"; sub = procs.Length + " procesos abiertos · " + Mb(total) + " de memoria en uso";
            SetButtons(null, "Actualizar");
            Section("Los que más memoria usan");
            foreach (var g in groups.Take(40))
            {
                string name = g.Name;
                string body = g.Count + (g.Count == 1 ? " proceso" : " procesos") + " · " + Mb(g.Mem);
                if (name.Equals("svchost", StringComparison.OrdinalIgnoreCase))
                    body += "\nservicios de Windows: no se cierran; agrúpalos con el ajuste «Agrupar procesos svchost».";
                var r = Row(name, body);
                if (Protected.Contains(name)) { r.Badge = "sistema"; r.BadgeStyle = 3; }
                else { r.Action = "cerrar"; r.ActionClick += delegate { KillGroup(name); }; }
            }
        }

        void KillGroup(string name)
        {
            if (MessageBox.Show("¿Cerrar todos los procesos «" + name + "»?\n\nSi es una app abierta, perderás lo que no hayas guardado.", "Opti Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int n = 0;
            foreach (var p in Process.GetProcessesByName(name))
                try { p.Kill(); n++; } catch { } finally { p.Dispose(); }
            Thread.Sleep(300);
            RefreshView(); Say(n > 0 ? "cerrado: " + name + "." : "Windows no dejó cerrar " + name + ".");
        }

        // ---------- inicio de windows ----------

        void BuildInicio()
        {
            List<StartupItem> items;
            try { items = Startup.Load(); } catch (Exception e) { items = new List<StartupItem>(); Say("no se pudo leer el inicio: " + e.Message); }
            title = "Inicio de Windows"; sub = items.Count + " apps · " + items.Count(i => i.Enabled) + " se abren al encender";
            SetButtons(null, "Actualizar");
            Section("Se abren al encender el equipo");
            if (items.Count == 0) Row("Nada que mostrar", "No hay apps configuradas para iniciarse con Windows.");
            foreach (var it in items)
            {
                var item = it;
                var r = Row(item.Name, item.Source + "\n" + item.Command);
                r.Badge = item.Enabled ? "activada" : "desactivada"; r.BadgeStyle = item.Enabled ? 1 : 3;
                r.Action = item.Enabled ? "desactivar" : "activar";
                r.ActionClick += delegate
                {
                    try { Startup.SetEnabled(item, !item.Enabled); Say((item.Enabled ? "desactivada: " : "activada: ") + item.Name + "."); }
                    catch (Exception e) { MessageBox.Show("No se pudo cambiar: " + e.Message, "Opti Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    RefreshView();
                };
            }
        }

        // ---------- herramientas ----------

        void Tool(string t, string body, string action, Action run)
        {
            var r = Row(t, body); r.Action = action;
            r.ActionClick += delegate { if (!busy) run(); };
        }

        void BuildHerramientas()
        {
            title = "Herramientas"; sub = "limpieza, restauración y más";
            SetButtons(null, null);
            Section("Seguridad");
            Tool("Crear punto de restauración", "Guarda el estado actual de Windows para volver atrás si algo falla. Opti Hub también crea uno antes de aplicar ajustes.", "crear", CreateRestorePoint);
            Tool("Deshacer todos los ajustes", "Vuelve a los valores que había antes de usar Opti Hub.", "deshacer", RevertAll);
            Section("Limpieza");
            Tool("Limpiar archivos temporales", "Borra la carpeta temporal de tu usuario y la de Windows. Los archivos en uso se saltan.", "limpiar", CleanTemp);
            Tool("Liberador de espacio de Windows", "Abre la herramienta de Windows para borrar actualizaciones antiguas, miniaturas y más.", "abrir", () => Open("cleanmgr.exe", ""));
            Section("Otras");
            Tool("Reiniciar el Explorador de Windows", "Útil si la barra de tareas o el escritorio van lentos o se quedan colgados.", "reiniciar", RestartExplorer);
            Tool("WinUtil de Chris Titus", "Abre la utilidad de Chris Titus Tech en PowerShell (se descarga de christitus.com). Tiene muchas más opciones avanzadas.", "abrir", OpenWinUtil);
        }

        void CreateRestorePoint()
        {
            Busy(true); Say("creando punto de restauración… (puede tardar un poco)");
            string err = null;
            Background(() => { err = App.CreateRestorePoint(); }, () =>
            {
                Busy(false);
                Say(err == null ? "punto de restauración creado." : "no se pudo crear el punto de restauración: " + err);
            });
        }

        void CleanTemp()
        {
            Busy(true); Say("limpiando archivos temporales…");
            long freed = 0;
            Background(() =>
            {
                freed += App.Clean(Path.GetTempPath());
                freed += App.Clean(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
            }, () => { Busy(false); Say("limpieza terminada: " + Mb(freed) + " liberados."); });
        }

        void RestartExplorer()
        {
            if (MessageBox.Show("La barra de tareas y el escritorio desaparecerán un momento y volverán solos. ¿Continuar?", "Opti Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var p in Process.GetProcessesByName("explorer")) try { p.Kill(); } catch { } finally { p.Dispose(); }
            Thread.Sleep(1500);
            if (Process.GetProcessesByName("explorer").Length == 0) Open("explorer.exe", "");
            Say("explorador reiniciado.");
        }

        void OpenWinUtil()
        {
            if (MessageBox.Show("Se abrirá PowerShell y ejecutará el script de christitus.com/win, descargado de internet.\n\nEs una herramienta conocida, pero Opti Hub no controla lo que hace ni puede deshacer sus cambios. ¿Continuar?",
                "Opti Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Open("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"irm https://christitus.com/win | iex\"");
            Say("WinUtil abierto en otra ventana.");
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
    }
}
