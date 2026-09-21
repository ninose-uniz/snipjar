using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
                        Bitmap shot = null;
                        try { shot = Crop(overlay.Selection, captured, virtualScreen); }
                        catch (Exception ex) { Toast.Notice("キャプチャできませんでした", ex.Message); }
                        finally
                        {
                            overlay.Dispose();
                            captured.Dispose();
                        }
                        if (shot == null) busy = false;
                        else Offer(shot, overlay.Selection);
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

        static Bitmap Crop(Rectangle selection, Bitmap full, Rectangle virtualScreen)
        {
            if (selection.Width <= 0 || selection.Height <= 0) return null; // cancelled

            Rectangle local = new Rectangle(
                selection.X - virtualScreen.X, selection.Y - virtualScreen.Y,
                selection.Width, selection.Height);
            return full.Clone(local, PixelFormat.Format24bppRgb);
        }

        // Nothing is copied, saved or sent until the bar is answered.
        static void Offer(Bitmap shot, Rectangle selection)
        {
            ActionBar bar = new ActionBar(selection);
            bar.FormClosed += delegate
            {
                ActionBar.Choice choice = bar.Result;
                host.BeginInvoke((Action)delegate
                {
                    try { Apply(choice, shot); }
                    catch (Exception ex) { Toast.Notice("処理できませんでした", ex.Message); }
                    finally
                    {
                        bar.Dispose();
                        shot.Dispose();
                        busy = false;
                    }
                });
            };
            bar.Show();
        }

        static void Apply(ActionBar.Choice choice, Bitmap shot)
        {
            if (choice == ActionBar.Choice.None) return; // thrown away on purpose

            byte[] png = ToPng(shot);
            bool saved = false;

            if (choice == ActionBar.Choice.Copy)
            {
                SetClipboardImage(shot);
                Toast.Copied(shot.Width + " × " + shot.Height);
            }
            else
            {
                Toast.Saved(SaveLocally(png));
                saved = true;
            }

            if (Config.UploadAlways) Send(png, ToThumbnail(shot), saved);
        }

        // Runs quietly in the background: the point of it is the gallery, so there
        // is nothing to report unless it fails and the shot would otherwise be lost.
        static void Send(byte[] png, byte[] thumbnail, bool alreadySaved)
        {
            Thread worker = new Thread(delegate()
            {
                Shot shot = null;
                string failure = null;
                try { shot = Uploader.Upload(png); }
                catch (Exception ex) { failure = ex.Message; }

                if (shot != null)
                {
                    // A small screenshot can compress worse as JPEG than as PNG;
                    // when that happens the gallery is better off with the original.
                    if (thumbnail.Length < png.Length)
                    {
                        try { Uploader.UploadThumb(shot.Key, thumbnail); }
                        catch { } // the gallery falls back to the full image
                    }
                    return;
                }

                string message = failure;
                host.BeginInvoke((Action)delegate
                {
                    if (alreadySaved) Toast.Notice("一覧には残せませんでした", message);
                    else Toast.Failure(message, SaveLocally(png));
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
                byte[] thumbnail;
                using (Bitmap bitmap = Grab(Screen.PrimaryScreen.Bounds))
                {
                    png = ToPng(bitmap);
                    thumbnail = ToThumbnail(bitmap);
                }
                Shot shot = Uploader.Upload(png);
                Uploader.UploadThumb(shot.Key, thumbnail);
                SetClipboard(shot.Url);
                WriteRunLog("OK " + shot.Url + " (" + png.Length + " bytes)");
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

        // Small enough that a page of the gallery is a few hundred KB, not tens of MB.
        static byte[] ToThumbnail(Bitmap source)
        {
            const int MaxWidth = 400;
            double scale = Math.Min(1.0, (double)MaxWidth / source.Width);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using (Bitmap small = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, width, height));
                }
                return ToJpeg(small, 72L);
            }
        }

        static byte[] ToJpeg(Bitmap bitmap, long quality)
        {
            ImageCodecInfo jpeg = null;
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.MimeType == "image/jpeg") { jpeg = codec; break; }
            }

            using (EncoderParameters settings = new EncoderParameters(1))
            using (MemoryStream buffer = new MemoryStream())
            {
                settings.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, quality);
                bitmap.Save(buffer, jpeg, settings);
                return buffer.ToArray();
            }
        }

        static string SaveLocally(byte[] png)
        {
            string directory = Config.SaveDir ?? Config.DefaultSaveDir;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            File.WriteAllBytes(path, png);
            return path;
        }

        // The clipboard is shared; another app can hold it for a moment.
        static void SetClipboard(string text)
        {
            Retry(delegate { Clipboard.SetText(text); });
        }

        static void SetClipboardImage(Bitmap bitmap)
        {
            Retry(delegate { Clipboard.SetImage(bitmap); });
        }

        static void Retry(Action action)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    action();
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
