using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Snipjar
{
    // Full-desktop dimmed overlay. The screen is already captured before this
    // opens, so the crop comes out of that bitmap and nothing can slip in between.
    //
    // Shown with Show(), never ShowDialog(): a modal loop here ends up tied to the
    // rest of the app's windows, and a toast from the previous capture closing was
    // enough to tear this down mid-drag. The caller waits on FormClosed instead.
    class Overlay : Form
    {
        const int MinDrag = 5;

        readonly Bitmap shot;     // untouched capture of the whole virtual desktop
        readonly Bitmap dimmed;   // same, pre-darkened, so painting stays cheap
        readonly Rectangle bounds;

        Point anchor;
        Point cursor;
        bool dragging;
        Rectangle painted = Rectangle.Empty;

        // Empty means the user cancelled.
        public Rectangle Selection = Rectangle.Empty; // screen coordinates

        public Overlay(Bitmap capture, Rectangle virtualScreen)
        {
            shot = capture;
            bounds = virtualScreen;
            dimmed = Darken(capture);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualScreen;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        static Bitmap Darken(Bitmap source)
        {
            Bitmap copy = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(copy))
            using (SolidBrush veil = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            {
                g.DrawImageUnscaled(source, 0, 0);
                g.FillRectangle(veil, 0, 0, copy.Width, copy.Height);
            }
            return copy;
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr window);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            // The resident process is not the one the user just clicked, so it has to
            // ask for the foreground explicitly; the launcher hands over the right.
            SetForegroundWindow(Handle);
            Activate();
            Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            anchor = e.Location;
            cursor = e.Location;
            dragging = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            cursor = e.Location;
            Rectangle now = Current();
            Rectangle dirty = painted.IsEmpty ? now : Rectangle.Union(painted, now);
            dirty.Inflate(90, 60); // room for the border and the size label
            Invalidate(dirty);
            painted = now;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            // Windows only synthesises WM_MOUSEMOVE when the queue is otherwise idle,
            // so the button-up can overtake the last move. Take the corner from here.
            cursor = e.Location;
            Rectangle rect = Current();
            if (rect.Width >= MinDrag && rect.Height >= MinDrag)
            {
                Selection = new Rectangle(
                    rect.X + bounds.X, rect.Y + bounds.Y, rect.Width, rect.Height);
            }
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
        }

        Rectangle Current()
        {
            return Rectangle.FromLTRB(
                Math.Min(anchor.X, cursor.X), Math.Min(anchor.Y, cursor.Y),
                Math.Max(anchor.X, cursor.X), Math.Max(anchor.Y, cursor.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(dimmed, 0, 0);

            Rectangle rect = dragging ? Current() : Rectangle.Empty;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            g.DrawImage(shot, rect, rect, GraphicsUnit.Pixel);
            using (Pen pen = new Pen(Color.FromArgb(230, 90, 170, 255), 1))
            {
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }
            DrawSize(g, rect);
        }

        void DrawSize(Graphics g, Rectangle rect)
        {
            string label = rect.Width + " × " + rect.Height;
            using (Font font = new Font("Yu Gothic UI", 9f))
            {
                SizeF size = g.MeasureString(label, font);
                float x = rect.X;
                float y = rect.Y - size.Height - 6;
                if (y < 2) y = rect.Y + 6; // near the top edge, sit inside instead
                RectangleF box = new RectangleF(x, y, size.Width + 12, size.Height + 4);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush back = new SolidBrush(Color.FromArgb(220, 20, 20, 22)))
                using (SolidBrush fore = new SolidBrush(Color.White))
                {
                    g.FillRectangle(back, box);
                    g.DrawString(label, font, fore, box.X + 6, box.Y + 2);
                }
                g.SmoothingMode = SmoothingMode.Default;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && dimmed != null) dimmed.Dispose();
            base.Dispose(disposing);
        }
    }
}
