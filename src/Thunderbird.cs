using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinCal
{
    public class TbCalendarData
    {
        public string Key;      // имя календаря (для отображения)
        public string Color;
        public List<EventItem> Value;
    }

    public class TbCalendarInfo
    {
        public string Id;
        public string Name;
        public string Type;
        public string Color;
        public string Uri;
        public bool CacheEnabled;
        public bool Disabled;
    }

    // Чтение календарей Thunderbird: prefs.js (реестр календарей) + calendar-data/local.sqlite / cache.sqlite.
    public static class Thunderbird
    {
        public static List<TbCalendarData> Load(ThunderbirdConfig cfg, DateTime windowStart, DateTime windowEnd, Logger log)
        {
            var profile = FindProfile(cfg.profile, log);
            if (profile == null) throw new Exception("профиль Thunderbird не найден (укажите путь в конфиге: thunderbird.profile)");
            log.Info("Thunderbird: профиль " + profile);

            var calendars = ReadRegistry(profile, log);
            if (calendars.Count == 0) throw new Exception("в prefs.js не найдено ни одного календаря");

            var result = new List<TbCalendarData>();
            foreach (var cal in calendars)
            {
                if (cal.Disabled) continue;
                if (cfg.calendars != null && cfg.calendars.Count > 0 && !cfg.calendars.Contains(cal.Name)) continue;
                try
                {
                    // календари типа ics хранят ссылку на файл/адрес — читаем напрямую
                    List<RawEvent> raws;
                    if (cal.Type == "ics" && !string.IsNullOrEmpty(cal.Uri))
                    {
                        raws = ReadIcsCalendar(cal, log);
                    }
                    else
                    {
                        raws = ReadEvents(profile, cal, log);
                    }
                    if (raws.Count == 0)
                    {
                        log.Info("Thunderbird: \"" + cal.Name + "\" (" + cal.Type + ") — локальных событий нет" +
                            (cal.Type == "caldav" ? " (включите офлайн-кэш календаря в Thunderbird, чтобы события сохранялись локально)" : ""));
                        continue;
                    }
                    var items = SyncEngine.ExpandToItems(raws, cal.Name, windowStart, windowEnd, -1, log);
                    result.Add(new TbCalendarData { Key = cal.Name, Color = cal.Color, Value = items });
                    log.Info("Thunderbird: \"" + cal.Name + "\" — событий в окне: " + items.Count);
                }
                catch (Exception ex)
                {
                    log.Error("Thunderbird: календарь \"" + cal.Name + "\": " + ex.Message);
                }
            }
            return result;
        }

        // ---------- профиль ----------

        public static string FindProfile(string configured, Logger log)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thunderbird");
            if (!string.IsNullOrEmpty(configured))
            {
                if (Path.IsPathRooted(configured) && Directory.Exists(configured)) return configured;
                var rel = Path.Combine(root, configured);
                if (Directory.Exists(rel)) return rel;
                log.Error("Thunderbird: указанный профиль не найден: " + configured + ", пробую автоопределение");
            }
            var ini = Path.Combine(root, "profiles.ini");
            if (!File.Exists(ini)) return null;
            string installDefault = null, markedDefault = null, firstProfile = null;
            string currentPath = null;
            bool currentIsInstall = false, currentMarked = false;
            foreach (var rawLine in File.ReadAllLines(ini))
            {
                var line = rawLine.Trim();
                bool newSection = line.StartsWith("[Install", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase);
                if (newSection)
                {
                    // завершить предыдущую секцию
                    if (currentPath != null)
                    {
                        if (currentIsInstall && installDefault == null) installDefault = currentPath;
                        else
                        {
                            if (currentMarked && markedDefault == null) markedDefault = currentPath;
                            if (firstProfile == null) firstProfile = currentPath;
                        }
                    }
                    currentIsInstall = line.StartsWith("[Install", StringComparison.OrdinalIgnoreCase);
                    currentPath = null;
                    currentMarked = false;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                    currentPath = val;
                else if (key.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentIsInstall)
                    {
                        if (installDefault == null && val.Length > 0) installDefault = val;
                    }
                    else if (val == "1") currentMarked = true;
                }
            }
            if (currentPath != null)
            {
                if (currentIsInstall && installDefault == null) installDefault = currentPath;
                else
                {
                    if (currentMarked && markedDefault == null) markedDefault = currentPath;
                    if (firstProfile == null) firstProfile = currentPath;
                }
            }

            string chosen = installDefault;
            if (chosen == null) chosen = markedDefault;
            if (chosen == null) chosen = firstProfile;
            if (chosen == null) return null;
            if (Path.IsPathRooted(chosen)) return Directory.Exists(chosen) ? chosen : null;
            var full = Path.Combine(root, chosen.Replace('/', '\\'));
            return Directory.Exists(full) ? full : null;
        }

        // ---------- реестр календарей из prefs.js ----------

        static readonly Regex PrefRegex = new Regex(
            "user_pref\\(\"calendar\\.registry\\.([^\"]+)\\.([^\".]+)\",\\s*(.+?)\\);",
            RegexOptions.Compiled);

        public static List<TbCalendarInfo> ReadRegistry(string profileDir, Logger log)
        {
            var res = new List<TbCalendarInfo>();
            var prefs = Path.Combine(profileDir, "prefs.js");
            if (!File.Exists(prefs)) return res;
            var map = new Dictionary<string, TbCalendarInfo>();
            foreach (var line in File.ReadAllLines(prefs))
            {
                var m = PrefRegex.Match(line);
                if (!m.Success) continue;
                var id = m.Groups[1].Value;
                var key = m.Groups[2].Value;
                var value = UnquotePref(m.Groups[3].Value.Trim());
                TbCalendarInfo info;
                if (!map.TryGetValue(id, out info))
                {
                    info = new TbCalendarInfo { Id = id };
                    map[id] = info;
                }
                switch (key)
                {
                    case "name": info.Name = value; break;
                    case "type": info.Type = value; break;
                    case "color": info.Color = value; break;
                    case "uri": info.Uri = value; break;
                    case "cache.enabled": info.CacheEnabled = value == "true"; break;
                    case "disabled": info.Disabled = value == "true"; break;
                }
            }
            foreach (var info in map.Values)
            {
                if (!string.IsNullOrEmpty(info.Name)) res.Add(info);
            }
            return res;
        }

        // календарь типа ics: uri = file:///C:/... или http(s)/webcal — берём напрямую
        static List<RawEvent> ReadIcsCalendar(TbCalendarInfo cal, Logger log)
        {
            var src = new IcsSource { name = cal.Name };
            var uri = cal.Uri;
            if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // file:///C:/path -> C:\path (URI-декодирование)
                var path = uri.Substring("file://".Length).TrimStart('/');
                path = Uri.UnescapeDataString(path).Replace('/', '\\');
                src.path = path;
            }
            else if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            {
                src.url = uri;
            }
            else
            {
                src.path = uri; // на всякий случай — трактуем как локальный путь
            }
            var text = Ics.Fetch(src, log);
            return Ics.Parse(text, log);
        }

        static string UnquotePref(string s)
        {
            if (s == "\"true\"") return "true";
            if (s == "\"false\"") return "false";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                var body = s.Substring(1, s.Length - 2);
                return body.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return s;
        }

        // ---------- события из sqlite ----------

        public static List<RawEvent> ReadEvents(string profileDir, TbCalendarInfo cal, Logger log)
        {
            var raws = new List<RawEvent>();
            var dataDir = Path.Combine(profileDir, "calendar-data");
            var dbs = new List<string> { Path.Combine(dataDir, "local.sqlite"), Path.Combine(dataDir, "cache.sqlite") };
            foreach (var db in dbs)
            {
                if (!File.Exists(db)) continue;
                try
                {
                    var copy = CopyDb(db);
                    using (var con = new SQLiteConnection("Data Source=" + copy + ";Version=3;"))
                    {
                        con.Open();
                        ReadFrom(con, cal.Id, raws, log);
                    }
                    TryDelete(copy);
                }
                catch (Exception ex)
                {
                    log.Error("Thunderbird: не удалось прочитать " + Path.GetFileName(db) + ": " + ex.Message);
                }
            }
            return raws;
        }

        static string CopyDb(string db)
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "wincal-tb");
            Directory.CreateDirectory(tmpDir);
            var copy = Path.Combine(tmpDir, Path.GetFileName(db));
            File.Copy(db, copy, true);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var side = db + suffix;
                if (File.Exists(side)) File.Copy(side, copy + suffix, true);
            }
            return copy;
        }

        static void TryDelete(string copy)
        {
            try { File.Delete(copy); } catch { }
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                try { if (File.Exists(copy + suffix)) File.Delete(copy + suffix); } catch { }
            }
        }

        static DateTime FromUs(long us)
        {
            return new DateTime(621355968000000000L + us * 10, DateTimeKind.Utc);
        }

        static void ReadFrom(SQLiteConnection con, string calId, List<RawEvent> raws, Logger log)
        {
            // локация и описание
            var props = new Dictionary<string, Dictionary<string, string>>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT item_id, key, value FROM cal_properties WHERE cal_id = @cal AND key IN ('LOCATION','DESCRIPTION')";
                cmd.Parameters.AddWithValue("@cal", calId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var itemId = r.GetString(0);
                        var key = r.GetString(1);
                        var val = r.IsDBNull(2) ? "" : ReadText(r.GetValue(2));
                        Dictionary<string, string> d;
                        if (!props.TryGetValue(itemId, out d)) { d = new Dictionary<string, string>(); props[itemId] = d; }
                        d[key] = val;
                    }
                }
            }

            // рекуррентность
            var recurrences = new Dictionary<string, string>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT item_id, icalString FROM cal_recurrence WHERE cal_id = @cal";
                cmd.Parameters.AddWithValue("@cal", calId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        recurrences[r.GetString(0)] = r.IsDBNull(1) ? "" : ReadText(r.GetValue(1));
                    }
                }
            }

            var mastersByUid = new Dictionary<string, List<RawEvent>>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT id, title, flags, event_start, event_end, event_start_tz, recurrence_id, ical_status FROM cal_events WHERE cal_id = @cal";
                cmd.Parameters.AddWithValue("@cal", calId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var ev = new RawEvent();
                        ev.Uid = r.GetString(0);
                        ev.Subject = r.IsDBNull(1) ? null : r.GetString(1);
                        var flags = r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2));
                        var startUs = r.IsDBNull(3) ? 0 : Convert.ToInt64(r.GetValue(3));
                        var endUs = r.IsDBNull(4) ? 0 : Convert.ToInt64(r.GetValue(4));
                        var startTz = r.IsDBNull(5) ? null : Convert.ToString(r.GetValue(5));
                        var recIdUs = r.IsDBNull(6) ? (long?)null : Convert.ToInt64(r.GetValue(6));
                        var status = r.IsDBNull(7) ? null : Convert.ToString(r.GetValue(7));

                        ev.AllDay = (flags & 64) != 0;
                        if (string.IsNullOrEmpty(startTz) || startTz == "none" || startTz == "floating") ev.Tz = "";
                        else ev.Tz = startTz;

                        var tzObj = Tz.Resolve(ev.Tz, null);
                        var startUtc = FromUs(startUs);
                        ev.Start = ev.AllDay ? startUtc.Date : TimeZoneInfo.ConvertTime(startUtc, tzObj);
                        if (endUs != 0)
                        {
                            var endUtc = FromUs(endUs);
                            ev.End = ev.AllDay ? endUtc.Date : TimeZoneInfo.ConvertTime(endUtc, tzObj);
                            if (ev.AllDay && ev.End <= ev.Start) ev.End = ev.Start.AddDays(1);
                        }
                        else ev.End = DateTime.MinValue;

                        Dictionary<string, string> p;
                        if (props.TryGetValue(ev.Uid, out p))
                        {
                            string v;
                            if (p.TryGetValue("LOCATION", out v)) ev.Location = v;
                            if (p.TryGetValue("DESCRIPTION", out v)) ev.Description = v;
                        }
                        ev.Cancelled = status == "CANCELLED";

                        if (recIdUs != null)
                        {
                            var ridUtc = FromUs(recIdUs.Value);
                            ev.RecurrenceId = ev.AllDay ? ridUtc.Date : ridUtc;
                        }

                        string icsRec;
                        if (recurrences.TryGetValue(ev.Uid, out icsRec) && !string.IsNullOrEmpty(icsRec))
                        {
                            ApplyRecurrenceIcal(ev, icsRec);
                        }

                        List<RawEvent> list;
                        if (!mastersByUid.TryGetValue(ev.Uid, out list)) { list = new List<RawEvent>(); mastersByUid[ev.Uid] = list; }
                        list.Add(ev);
                    }
                }
            }
            foreach (var kv in mastersByUid) raws.AddRange(kv.Value);
        }

        static string ReadText(object o)
        {
            if (o is string) return (string)o;
            var bytes = o as byte[];
            if (bytes != null) return Encoding.UTF8.GetString(bytes);
            return o == null ? "" : o.ToString();
        }

        // icalString TB: строки вида "RRULE:FREQ=WEEKLY;BYDAY=MO" / "EXDATE;TZID=...:..." / "RDATE:..."
        // Разбираем тем же парсером, обернув в мини-VCALENDAR с DTSTART события.
        static void ApplyRecurrenceIcal(RawEvent ev, string icalString)
        {
            var sb = new StringBuilder();
            sb.Append("BEGIN:VCALENDAR\nBEGIN:VEVENT\n");
            sb.Append("UID:").Append(ev.Uid).Append('\n');
            if (ev.AllDay)
                sb.Append("DTSTART;VALUE=DATE:").Append(ev.Start.ToString("yyyyMMdd")).Append('\n');
            else if (ev.Tz == "UTC")
                sb.Append("DTSTART:").Append(ev.Start.ToString("yyyyMMdd'T'HHmmss'Z'")).Append('\n');
            else if (ev.Tz.Length > 0)
                sb.Append("DTSTART;TZID=").Append(ev.Tz).Append(':').Append(ev.Start.ToString("yyyyMMdd'T'HHmmss")).Append('\n');
            else
                sb.Append("DTSTART:").Append(ev.Start.ToString("yyyyMMdd'T'HHmmss")).Append('\n');
            foreach (var line in icalString.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                var up = t.ToUpperInvariant();
                if (up.StartsWith("RRULE") || up.StartsWith("EXDATE") || up.StartsWith("RDATE"))
                    sb.Append(t).Append('\n');
            }
            sb.Append("END:VEVENT\nEND:VCALENDAR\n");
            var parsed = Ics.Parse(sb.ToString(), null);
            if (parsed.Count > 0)
            {
                ev.Rrule = parsed[0].Rrule;
                ev.ExDates = parsed[0].ExDates;
                ev.RDates = parsed[0].RDates;
            }
        }
    }
}
