using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Shotlink
{
    // Bottom-right notification. Deliberately not a tray balloon: the app keeps
    // no notification-area icon, so this is drawn and owned by us.
    class Toast : Form
    {
        const int Width_ = 372;
        const int Height_ = 78;
        const int Margin_ = 16;
        const int HoldMs = 3600;

        readonly string headline;
        readonly string detail;
        readonly string link;     // null when there is nothing to open
        readonly Color accent;

        Timer hold;
        Timer fade;

        public static void Success(string url)
        {
            Show_("アップロードしました ✓", url, url, Color.FromArgb(94, 200, 130));
        }

        public static void Failure(string message, string savedPath)
        {
            Show_("アップロード失敗 — ローカルに保存しました", message, savedPath,
                Color.FromArgb(235, 130, 110));
        }

        public static void Notice(string headline, string detail)
        {
            Show_(headline, detail, null, Color.FromArgb(140, 150, 165));
        }

        static void Show_(string headline, string detail, string link, Color accent)
        {
            Toast toast = new Toast(headline, detail, link, accent);
            toast.Show();
        }

        Toast(string headline, string detail, string link, Color accent)
        {
            this.headline = headline;
            this.detail = detail;
            this.link = link;
            this.accent = accent;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 29, 32);
            Size = new Size(Width_, Height_);
            DoubleBuffered = true;
            Opacity = 0.0;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width_ - Margin_, area.Bottom - Height_ - Margin_);
            Cursor = link != null ? Cursors.Hand : Cursors.Default;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FadeTo(1.0, 18, delegate { });

            hold = new Timer();
            hold.Interval = HoldMs;
            hold.Tick += delegate
            {
                hold.Stop();
                FadeTo(0.0, -14, delegate { Close(); });
            };
            hold.Start();
        }

        void FadeTo(double target, int stepPercent, Action done)
        {
            if (fade != null) fade.Stop();
            fade = new Timer();
            fade.Interval = 15;
            fade.Tick += delegate
            {
                double next = Opacity + stepPercent / 100.0;
                bool finished = stepPercent > 0 ? next >= target : next <= target;
                Opacity = finished ? target : next;
                if (finished)
                {
                    fade.Stop();
                    done();
                }
            };
            fade.Start();
        }

        protected override void OnClick(EventArgs e)
        {
            if (link != null)
            {
                try { Process.Start(link); }
                catch { }
            }
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush edge = new SolidBrush(Color.FromArgb(70, 72, 78)))
            {
                g.FillRectangle(edge, 0, 0, Width, Height);
            }
            using (SolidBrush back = new SolidBrush(BackColor))
            {
                g.FillRectangle(back, 1, 1, Width - 2, Height - 2);
            }
            using (SolidBrush bar = new SolidBrush(accent))
            {
                g.FillRectangle(bar, 1, 1, 4, Height - 2);
            }

            using (Font title = new Font("Yu Gothic UI", 10f, FontStyle.Bold))
            using (Font body = new Font("Yu Gothic UI", 8.5f))
            using (SolidBrush fore = new SolidBrush(Color.FromArgb(240, 241, 244)))
            using (SolidBrush sub = new SolidBrush(Color.FromArgb(158, 162, 170)))
            {
                Rectangle head = new Rectangle(18, 13, Width - 32, 22);
                Rectangle rest = new Rectangle(18, 38, Width - 32, 34);
                TextRenderer.DrawText(g, headline, title, head, fore.Color,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, detail, body, rest, sub.Color,
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
