using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinCal
{
    public class IcsSource
    {
        public string name = "";
        public string url = "";
        public string path = "";
        public string user = "";
        public string password = "";
        public string color = "#0078D7";
        public bool enabled = true;
        public int reminderMinutes = -1;
    }

    public class ThunderbirdConfig
    {
        public bool enabled = false;
        // Пусто = автоопределение профиля из profiles.ini
        public string profile = "";
        // null/пусто = все не отключённые календари; иначе список имён
        public List<string> calendars;
    }

    public class AppConfig
    {
        public int syncIntervalMinutes = 15;
        public int daysForward = 180;
        public int daysBackward = 7;
        public string prefix = "";
        public bool autostart = true;
        public List<IcsSource> sources = new List<IcsSource>();
        public ThunderbirdConfig thunderbird = new ThunderbirdConfig();
    }

    public static class ConfigStore
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCalendar"); }
        }
        public static string ConfigPath { get { return Path.Combine(Dir, "config.json"); } }
        public static string StatePath { get { return Path.Combine(Dir, "state.json"); } }
        public static string LogPath { get { return Path.Combine(Dir, "log.txt"); } }

        public static AppConfig Load(Logger log)
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var def = new AppConfig();
                    WriteSampleIcs();
                    def.sources.Add(new IcsSource { name = "Пример", color = "#0078D7", path = Path.Combine(Dir, "sample.ics") });
                    Save(def);
                    if (log != null) log.Info("Создан конфиг по умолчанию: " + ConfigPath);
                    return def;
                }
                var ser = new JavaScriptSerializer();
                var cfg = ser.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                if (cfg == null) throw new Exception("пустой конфиг");
                if (cfg.sources == null) cfg.sources = new List<IcsSource>();
                if (cfg.thunderbird == null) cfg.thunderbird = new ThunderbirdConfig();
                return cfg;
            }
            catch (Exception ex)
            {
                if (log != null) log.Error("Ошибка чтения конфига, используются значения по умолчанию: " + ex.Message);
                return new AppConfig();
            }
        }

        public static void Save(AppConfig cfg)
        {
            Directory.CreateDirectory(Dir);
            var ser = new JavaScriptSerializer();
            File.WriteAllText(ConfigPath, ser.Serialize(cfg), new UTF8Encoding(true));
        }

        static void WriteSampleIcs()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var p = Path.Combine(Dir, "sample.ics");
                var sb = new StringBuilder();
                sb.AppendLine("BEGIN:VCALENDAR");
                sb.AppendLine("VERSION:2.0");
                sb.AppendLine("PRODID:-//wincal//sample//RU");
                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine("UID:wincal-sample-daily@local");
                sb.AppendLine("DTSTAMP:20260101T000000Z");
                sb.AppendLine("SUMMARY:Пример: каждый день в 9:00");
                sb.AppendLine("DTSTART;TZID=Europe/Samara:20260101T090000");
                sb.AppendLine("DTEND;TZID=Europe/Samara:20260101T093000");
                sb.AppendLine("RRULE:FREQ=DAILY");
                sb.AppendLine("END:VEVENT");
                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine("UID:wincal-sample-allday@local");
                sb.AppendLine("DTSTAMP:20260101T000000Z");
                sb.AppendLine("SUMMARY:Пример: весь день");
                sb.AppendLine("DTSTART;VALUE=DATE:20260105");
                sb.AppendLine("DTEND;VALUE=DATE:20260106");
                sb.AppendLine("RRULE:FREQ=MONTHLY;BYMONTHDAY=5");
                sb.AppendLine("END:VEVENT");
                sb.AppendLine("END:VCALENDAR");
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
                // подставить sample.ics в единственный источник, если путь пуст
            }
            catch { }
        }

        public static Dictionary<string, Dictionary<string, string>> LoadState()
        {
            try
            {
                if (!File.Exists(StatePath)) return new Dictionary<string, Dictionary<string, string>>();
                var ser = new JavaScriptSerializer();
                return ser.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(StatePath))
                    ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch { return new Dictionary<string, Dictionary<string, string>>(); }
        }

        public static void SaveState(Dictionary<string, Dictionary<string, string>> state)
        {
            Directory.CreateDirectory(Dir);
            var ser = new JavaScriptSerializer();
            File.WriteAllText(StatePath, ser.Serialize(state), new UTF8Encoding(true));
        }
    }

    public class Logger
    {
        readonly object gate = new object();
        public void Info(string msg) { Write("INFO", msg); }
        public void Error(string msg) { Write("ERR ", msg); }

        protected virtual void OnLine(string line) { }

        void Write(string level, string msg)
        {
            OnLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + msg);
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(ConfigStore.Dir);
                    var fi = new FileInfo(ConfigStore.LogPath);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        var lines = File.ReadAllLines(ConfigStore.LogPath);
                        var tail = new string[Math.Min(lines.Length, 500)];
                        Array.Copy(lines, lines.Length - tail.Length, tail, 0, tail.Length);
                        File.WriteAllLines(ConfigStore.LogPath, tail);
                    }
                    File.AppendAllText(ConfigStore.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + msg + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}
