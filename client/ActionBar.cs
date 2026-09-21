using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Shotlink
{
    // The small bar that appears next to the selection once the drag ends.
    // Nothing happens to the capture until one of these is pressed.
    class ActionBar : Form
    {
        public enum Choice { None, Copy, Save }

        const int ButtonWidth = 98;
        const int ButtonHeight = 32;
        const int Pad = 8;
        const int Gap = 6;
        const int Offset = 8; // distance from the selection edge

        public Choice Result = Choice.None;

        readonly Button copy;
        readonly Button save;
        Timer settle; // ignore the stray deactivate that can arrive right after Show

        public ActionBar(Rectangle selection)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 29, 32);
            ClientSize = new Size(ButtonWidth * 2 + Gap + Pad * 2, ButtonHeight + Pad * 2);

            copy = MakeButton("コピー", Pad);
            save = MakeButton("保存", Pad + ButtonWidth + Gap);
            copy.Click += delegate { Finish(Choice.Copy); };
            save.Click += delegate { Finish(Choice.Save); };
            Controls.Add(copy);
            Controls.Add(save);

            Location = Place(selection, Size);
        }

        Button MakeButton(string label, int x)
        {
            Button button = new Button();
            button.Text = label;
            button.SetBounds(x, Pad, ButtonWidth, ButtonHeight);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(70, 72, 78);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 50, 56);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 63, 70);
            button.BackColor = Color.FromArgb(36, 38, 42);
            button.ForeColor = Color.FromArgb(238, 240, 244);
            button.Font = new Font("Yu Gothic UI", 9.5f);
            button.TabStop = false;
            return button;
        }

        // Below the selection by preference, above it when there is no room,
        // and tucked inside the bottom edge when the selection fills the screen.
        static Point Place(Rectangle selection, Size size)
        {
            Rectangle area = Screen.FromRectangle(selection).WorkingArea;

            int y = selection.Bottom + Offset;
            if (y + size.Height > area.Bottom)
            {
                y = selection.Top - Offset - size.Height;
                if (y < area.Top) y = Math.Max(area.Top, selection.Bottom - size.Height - Offset);
            }

            int x = selection.Right - size.Width;
            if (x + size.Width > area.Right) x = area.Right - size.Width - 4;
            if (x < area.Left) x = area.Left + 4;

            return new Point(x, y);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — stay out of Alt+Tab
                return cp;
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr window);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            SetForegroundWindow(Handle);
            Activate();

            settle = new Timer();
            settle.Interval = 400;
            settle.Tick += delegate
            {
                settle.Stop();
                settle.Dispose();
                settle = null;
            };
            settle.Start();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (settle != null) return; // too early to be a real click elsewhere
            Finish(Choice.None);        // clicking away throws the capture out
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Finish(Choice.None);
            else if (e.KeyCode == Keys.C) Finish(Choice.Copy);
            else if (e.KeyCode == Keys.S) Finish(Choice.Save);
        }

        bool done;

        void Finish(Choice choice)
        {
            if (done) return; // closing already deactivates us, which lands here again
            done = true;
            Result = choice;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen edge = new Pen(Color.FromArgb(70, 72, 78)))
            {
                e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
