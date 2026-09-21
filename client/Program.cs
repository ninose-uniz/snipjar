using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Shotlink
{
    static class Program
    {
        const string InstanceName = @"Local\shotlink.instance";
        const string CaptureName = @"Local\shotlink.capture";
        const string QuitName = @"Local\shotlink.quit";

        static Mutex instance;
        static EventWaitHandle captureSignal;
        static EventWaitHandle quitSignal;
        static Form host;
        static bool busy;

        [STAThread]
        static void Main(string[] args)
        {
            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasFlag(args, "--quit")) { Signal(QuitName); return; }
            if (HasFlag(args, "--capture-full"))
            {
                Environment.ExitCode = CaptureWholeScreen() ? 0 : 1;
                return;
            }

            bool first;
            instance = new Mutex(true, InstanceName, out first);
            if (!first)
            {
                // This is the click on the pinned taskbar icon: hand our foreground
                // right to the resident copy, nudge it, and get out before Windows
                // gives this throwaway process a taskbar button.
                AllowSetForegroundWindow(AsfwAny);
                Signal(CaptureName);
                return;
            }

            string error;
            if (!Config.Load(out error))
            {
                MessageBox.Show(error, "shotlink", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            captureSignal = new EventWaitHandle(false, EventResetMode.AutoReset, CaptureName);
            quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);

            host = new Form();
            host.FormBorderStyle = FormBorderStyle.None;
            host.ShowInTaskbar = false;
            host.StartPosition = FormStartPosition.Manual;
            host.Location = new Point(-32000, -32000);
            host.Size = new Size(1, 1);
            IntPtr handle = host.Handle; // force creation so BeginInvoke works
            GC.KeepAlive(handle);

            StartWaiter();

            // Started from the icon rather than at logon, and nothing was resident
            // yet: the user still wants the capture they just asked for.
            if (!HasFlag(args, "--background")) host.BeginInvoke((Action)Capture);

            Application.Run(new ApplicationContext());
        }

        static void StartWaiter()
        {
            Thread waiter = new Thread(delegate()
            {
                WaitHandle[] handles = new WaitHandle[] { captureSignal, quitSignal };
                while (true)
                {
                    int index = WaitHandle.WaitAny(handles);
                    if (index == 1)
                    {
                        host.BeginInvoke((Action)delegate { Application.Exit(); });
                        return;
                    }
                    host.BeginInvoke((Action)Capture);
                }
            });
            waiter.IsBackground = true;
            waiter.Start();
        }

        static void Capture()
        {
            if (busy) return;
            busy = true;

            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Bitmap full = null;
            try
            {
                full = Grab(virtualScreen);
                Bitmap captured = full;
                Overlay overlay = new Overlay(captured, virtualScreen);
                overlay.FormClosed += delegate
                {
                    // Step out of the close before touching anything the form owns.
                    host.BeginInvoke((Action)delegate
                    {
                        try { Crop(overlay.Selection, captured, virtualScreen); }
                        catch (Exception ex) { Toast.Notice("キャプチャできませんでした", ex.Message); }
                        finally
                        {
                            overlay.Dispose();
                            captured.Dispose();
                            busy = false;
                        }
                    });
                };
                overlay.Show();
                full = null; // the handler above owns it now
            }
            catch (Exception ex)
            {
                if (full != null) full.Dispose();
                busy = false;
                Toast.Notice("キャプチャできませんでした", ex.Message);
            }
        }

        static void Crop(Rectangle selection, Bitmap full, Rectangle virtualScreen)
        {
            if (selection.Width <= 0 || selection.Height <= 0) return; // cancelled

            Rectangle local = new Rectangle(
                selection.X - virtualScreen.X, selection.Y - virtualScreen.Y,
                selection.Width, selection.Height);

            byte[] png;
            using (Bitmap crop = full.Clone(local, PixelFormat.Format24bppRgb))
            {
                png = ToPng(crop);
            }
            Send(png);
        }

        static void Send(byte[] png)
        {
            Thread worker = new Thread(delegate()
            {
                string url = null;
                string failure = null;
                try { url = Uploader.Upload(png); }
                catch (Exception ex) { failure = ex.Message; }

                string resultUrl = url;
                string resultError = failure;
                host.BeginInvoke((Action)delegate
                {
                    if (resultUrl != null)
                    {
                        SetClipboard(resultUrl);
                        Toast.Success(resultUrl);
                    }
                    else
                    {
                        string saved = SaveLocally(png);
                        SetClipboard(saved);
                        Toast.Failure(resultError, saved);
                    }
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        // Headless path used for checking the capture-upload chain end to end.
        static bool CaptureWholeScreen()
        {
            string error;
            if (!Config.Load(out error)) { WriteRunLog("NG " + error); return false; }
            try
            {
                byte[] png;
                using (Bitmap bitmap = Grab(Screen.PrimaryScreen.Bounds)) { png = ToPng(bitmap); }
                string url = Uploader.Upload(png);
                SetClipboard(url);
                WriteRunLog("OK " + url + " (" + png.Length + " bytes)");
                return true;
            }
            catch (Exception ex)
            {
                WriteRunLog("NG " + ex.Message);
                return false;
            }
        }

        static Bitmap Grab(Rectangle area)
        {
            Bitmap bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        static byte[] ToPng(Bitmap bitmap)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                bitmap.Save(buffer, ImageFormat.Png);
                return buffer.ToArray();
            }
        }

        static string SaveLocally(byte[] png)
        {
            Directory.CreateDirectory(Config.FallbackDir);
            string path = Path.Combine(Config.FallbackDir,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            File.WriteAllBytes(path, png);
            return path;
        }

        // The clipboard is shared; another app can hold it for a moment.
        static void SetClipboard(string text)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch
                {
                    Thread.Sleep(80);
                }
            }
        }

        static void WriteRunLog(string line)
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(Path.Combine(Config.Dir, "last-run.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { }
        }

        static void Signal(string name)
        {
            EventWaitHandle handle;
            if (EventWaitHandle.TryOpenExisting(name, out handle))
            {
                using (handle) { handle.Set(); }
            }
        }

        static bool HasFlag(string[] args, string flag)
        {
            return Array.IndexOf(args, flag) >= 0;
        }

        const uint AsfwAny = 0xFFFFFFFF;

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(uint processId);

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static void EnableDpiAwareness()
        {
            try
            {
                // PER_MONITOR_AWARE_V2 keeps coordinates in real pixels across mixed-DPI monitors.
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch { }
        }
    }
}
