using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.ApplicationModel.Appointments;

namespace WinCal
{
    static class Program
    {
        const string RunValueName = "WinCalendarAgenda";

        [STAThread]
        static void Main(string[] args)
        {
            var log = new Logger();
            if (args != null && args.Length > 0)
            {
                var cmd = args[0].ToLowerInvariant();
                try
                {
                    if (cmd == "sync-now")
                    {
                        var cfg = ConfigStore.Load(log);
                        new SyncEngine(cfg, log).SyncAll();
                        return;
                    }
                    if (cmd == "delete-calendar" && args.Length > 1)
                    {
                        var cfg = ConfigStore.Load(log);
                        new SyncEngine(cfg, log).DeleteCalendar(args[1]);
                        return;
                    }
                    if (cmd == "list")
                    {
                        ListStore(log);
                        return;
                    }
                    if (cmd == "icon-test")
                    {
                        Directory.CreateDirectory("icon-test");
                        using (var b = TrayApp.RenderIconBitmap(16)) b.Save("icon-test\\icon16.png", System.Drawing.Imaging.ImageFormat.Png);
                        using (var b = TrayApp.RenderIconBitmap(32)) b.Save("icon-test\\icon32.png", System.Drawing.Imaging.ImageFormat.Png);
                        using (var b = TrayApp.RenderIconBitmap(48)) b.Save("icon-test\\icon48.png", System.Drawing.Imaging.ImageFormat.Png);
                        // проверка, что ICO собирается и читается System.Drawing
                        using (var ms = TrayApp.BuildIconFile())
                        using (var ic = new Icon(ms))
                            Console.WriteLine("ico ok: " + ic.Width + "x" + ic.Height + ", frames=" + ms.Length + "B");
                        Console.WriteLine("icon-test written to " + Path.GetFullPath("icon-test"));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex.ToString());
                    Environment.ExitCode = 1;
                    return;
                }
            }

            bool createdNew;
            var mutex = new Mutex(true, "WinCalendarAgendaSingleInstance", out createdNew);
            if (!createdNew) return;

            Application.EnableVisualStyles();
            Application.Run(new TrayApp(log));
        }

        static void ListStore(Logger log)
        {
            var store = Rt.Await(AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite));
            foreach (var cal in Rt.Await(store.FindAppointmentCalendarsAsync()))
            {
                int count = 0;
                try
                {
                    var found = Rt.Await(cal.FindAppointmentsAsync(DateTimeOffset.Now, new TimeSpan(400, 0, 0, 0)));
                    count = found.Count;
                }
                catch { }
                log.Info("Календарь \"" + cal.DisplayName + "\" (id=" + cal.LocalId + ", source=" + cal.SourceDisplayName + "): событий в ближайший год — " + count);
            }
        }

        public static bool IsAutostartEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (k == null) return false;
                return k.GetValue(RunValueName) != null;
            }
        }

        public static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                else
                {
                    try { k.DeleteValue(RunValueName, false); } catch { }
                }
            }
        }
    }

    public class TrayApp : ApplicationContext
    {
        readonly Logger log;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem statusItem;
        readonly ToolStripMenuItem syncItem;
        readonly ToolStripMenuItem autostartItem;
        readonly AutoResetEvent kick = new AutoResetEvent(false);
        readonly System.Windows.Forms.Timer uiTimer;
        Thread worker;
        volatile bool stop;
        volatile string pendingBalloon;
        volatile bool pendingBalloonError;
        DateTime cfgStamp;
        string lastStatus = "синхронизация…";
        int iconDay = -1;

        public TrayApp(Logger log)
        {
            this.log = log;

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Запуск…") { Enabled = false };
            syncItem = new ToolStripMenuItem("Синхронизировать сейчас");
            syncItem.Click += delegate { kick.Set(); };
            var settingsItem = new ToolStripMenuItem("Настройки…");
            settingsItem.Click += delegate { OpenSettings(); };
            var configItem = new ToolStripMenuItem("Открыть конфиг (текстом)");
            configItem.Click += delegate { OpenConfig(); };
            var logItem = new ToolStripMenuItem("Открыть лог");
            logItem.Click += delegate { OpenLog(); };
            autostartItem = new ToolStripMenuItem("Автозапуск");
            autostartItem.Click += delegate { ToggleAutostart(); };
            var exitItem = new ToolStripMenuItem("Выход");
            exitItem.Click += delegate { ExitApp(); };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(syncItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(configItem);
            menu.Items.Add(logItem);
            menu.Items.Add(autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            tray = new NotifyIcon();
            tray.Icon = CreateIcon();
            iconDay = DateTime.Now.Day;
            tray.Text = "WinCalendar — события в часах\nЛКМ — обновить, ПКМ — меню";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.Click += delegate { kick.Set(); }; // одиночный ЛКМ — синхронизировать

            cfgStamp = ConfigExists() ? File.GetLastWriteTime(ConfigStore.ConfigPath) : DateTime.MinValue;
            var cfg = ConfigStore.Load(log);
            if (cfg.autostart && !Program.IsAutostartEnabled()) Program.SetAutostart(true);
            autostartItem.Checked = Program.IsAutostartEnabled();

            worker = new Thread(WorkerLoop);
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 2000;
            uiTimer.Tick += UiTick;
            uiTimer.Start();
        }

        static bool ConfigExists()
        {
            return File.Exists(ConfigStore.ConfigPath);
        }

        void UiTick(object sender, EventArgs e)
        {
            if (DateTime.Now.Day != iconDay)
            {
                // наступил новый день — перерисовать число на иконке
                iconDay = DateTime.Now.Day;
                var old = tray.Icon;
                tray.Icon = CreateIcon();
                if (old != null) old.Dispose();
            }
            statusItem.Text = lastStatus;
            if (pendingBalloon != null)
            {
                var text = pendingBalloon;
                pendingBalloon = null;
                tray.ShowBalloonTip(4000, "WinCalendar", text,
                    pendingBalloonError ? ToolTipIcon.Error : ToolTipIcon.Info);
            }
        }

        void WorkerLoop()
        {
            int minutes = 15;
            while (!stop)
            {
                try
                {
                    var cfg = ConfigStore.Load(log);
                    minutes = cfg.syncIntervalMinutes;
                    if (minutes < 5) minutes = 5;
                    var engine = new SyncEngine(cfg, log);
                    var summary = engine.SyncAll();
                    lastStatus = DateTime.Now.ToString("HH:mm") + " — " + summary;
                    pendingBalloon = null; // успешная синхронизация — без всплывашек
                }
                catch (Exception ex)
                {
                    log.Error("Синхронизация: " + ex.Message);
                    lastStatus = DateTime.Now.ToString("HH:mm") + " — ошибка: " + ex.Message;
                    pendingBalloon = "Ошибка синхронизации: " + ex.Message;
                    pendingBalloonError = true;
                }
                // ждём интервал или сигнал немедленной синхронизации; заодно следим за конфигом
                var waited = 0;
                while (waited < minutes * 60 * 1000 && !stop)
                {
                    if (kick.WaitOne(1000))
                    {
                        // проверка: изменился ли конфиг с прошлого раза
                        if (ConfigExists())
                        {
                            var stamp = File.GetLastWriteTime(ConfigStore.ConfigPath);
                            if (stamp != cfgStamp) { cfgStamp = stamp; break; }
                        }
                        break;
                    }
                    waited += 1000;
                    if (ConfigExists())
                    {
                        var stamp2 = File.GetLastWriteTime(ConfigStore.ConfigPath);
                        if (stamp2 != cfgStamp) { cfgStamp = stamp2; break; }
                    }
                }
            }
        }

        void OpenSettings()
        {
            try
            {
                var cur = ConfigStore.Load(log); // создать, если ещё нет
                using (var f = new SettingsForm(cur))
                {
                    if (f.ShowDialog() == DialogResult.OK)
                    {
                        ConfigStore.Save(cur);
                        kick.Set(); // синхронизировать с новым конфигом немедленно
                    }
                }
            }
            catch (Exception ex) { log.Error("Настройки: " + ex.Message); }
        }

        void OpenConfig()
        {
            try
            {
                ConfigStore.Load(log); // создать, если ещё нет
                System.Diagnostics.Process.Start("notepad.exe", ConfigStore.ConfigPath);
            }
            catch (Exception ex) { log.Error("Открытие конфига: " + ex.Message); }
        }

        void OpenLog()
        {
            try { System.Diagnostics.Process.Start("notepad.exe", ConfigStore.LogPath); }
            catch (Exception ex) { log.Error("Открытие лога: " + ex.Message); }
        }

        void ToggleAutostart()
        {
            var now = Program.IsAutostartEnabled();
            Program.SetAutostart(!now);
            autostartItem.Checked = !now;
            var cfg = ConfigStore.Load(log);
            cfg.autostart = !now;
            ConfigStore.Save(cfg);
        }

        void ExitApp()
        {
            stop = true;
            kick.Set();
            try { if (worker != null && worker.Join(3000)) worker = null; } catch { }
            tray.Visible = false;
            Application.Exit();
        }

        internal static Icon CreateIcon()
        {
            // многоразмерная иконка (16+32): чётко и в трее, и на высоких DPI.
            // Внимание: System.Drawing.Icon не понимает PNG-фреймы в ICO —
            // фреймы собираются в классическом BMP-формате.
            using (var ms = BuildIconFile())
            {
                return new Icon(ms);
            }
        }

        internal static MemoryStream BuildIconFile()
        {
            var f16 = BmpFrame(RenderIconBitmap(16));
            var f32 = BmpFrame(RenderIconBitmap(32));
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((ushort)0);          // reserved
            w.Write((ushort)1);          // type: icon
            w.Write((ushort)2);          // frames
            WriteFrameHeader(w, 16, f16.Length, 38);  // 6 (ICONDIR) + 2 * 16 (ICONDIRENTRY)
            WriteFrameHeader(w, 32, f32.Length, 38 + f16.Length);
            w.Write(f16);
            w.Write(f32);
            w.Flush();
            ms.Position = 0;
            return ms;
        }

        static void WriteFrameHeader(BinaryWriter w, int size, int len, int offset)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);        // colors
            w.Write((byte)0);        // reserved
            w.Write((ushort)1);      // planes
            w.Write((ushort)32);     // bpp
            w.Write((uint)len);
            w.Write((uint)offset);
        }

        internal static Bitmap RenderIconBitmap(int s)
        {
            var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float k = s / 16f;
                using (var body = new SolidBrush(Color.White))
                    g.FillRectangle(body, 1f * k, 3f * k, 14f * k, 12f * k);
                using (var header = new SolidBrush(Color.FromArgb(0, 120, 215)))
                    g.FillRectangle(header, 1f * k, 3f * k, 14f * k, 4f * k);
                using (var pen = new Pen(Color.FromArgb(120, 120, 120), Math.Max(1f, k)))
                    g.DrawRectangle(pen, 1f * k, 3f * k, 14f * k, 12f * k);
                var text = DateTime.Now.Day.ToString();
                using (var f = new Font("Segoe UI", s * 0.46f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var dark = new SolidBrush(Color.FromArgb(40, 40, 40)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(text, f, dark, new RectangleF(1f * k, 6f * k, 14f * k, 9f * k), sf);
                }
            }
            return bmp;
        }

        // Bitmap -> ICO-фрейм: BITMAPINFOHEADER + XOR (BGRA, снизу вверх) + AND-маска
        static byte[] BmpFrame(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(40u);                       // biSize
            bw.Write((uint)w);                   // biWidth
            bw.Write((uint)(h * 2));             // biHeight (XOR + AND)
            bw.Write((ushort)1);                 // biPlanes
            bw.Write((ushort)32);                // biBitCount
            bw.Write(0u);                        // biCompression
            bw.Write((uint)(w * h * 4));         // biSizeImage
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var px = new byte[w * h * 4];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            for (int y = h - 1; y >= 0; y--)
                bw.Write(px, y * w * 4, w * 4);

            int rowBytes = ((w + 31) / 32) * 4;  // AND-маска 1bpp, выровнена на 32 бита
            var zeros = new byte[rowBytes];
            for (int y = 0; y < h; y++)
                bw.Write(zeros);

            bw.Flush();
            return ms.ToArray();
        }
    }
}
