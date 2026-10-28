using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace WinCal
{
    public class EventItem
    {
        public string Key;              // стабильный ключ экземпляра
        public string Subject;
        public string Location;
        public string Description;
        public DateTimeOffset Start;
        public TimeSpan Duration;
        public bool AllDay;
        public int ReminderMinutes = -1;
    }

    // Событие «как в файле», до раскрытия повторов. Start/End — настенное время в Tz.
    public class RawEvent
    {
        public string Uid;
        public string Subject;
        public string Location;
        public string Description;
        public string Tz;               // "" = плавающее (локальное), "UTC" = абсолютное
        public DateTime Start;
        public DateTime End;
        public bool AllDay;
        public string Rrule;            // "FREQ=DAILY;..." или null
        public List<DateTime> ExDates = new List<DateTime>();  // all-day: даты; иначе — UTC-моменты
        public List<DateTime> RDates = new List<DateTime>();   // то же
        public DateTime? RecurrenceId;  // all-day: дата; иначе UTC-момент
        public bool Cancelled;
        public bool IsOverride { get { return RecurrenceId != null; } }
    }

    public static class Tz
    {
        static readonly Dictionary<string, string> IanaToWindows = BuildMap();
        static TimeZoneInfo _local = TimeZoneInfo.Local;
        public static TimeZoneInfo Utc = TimeZoneInfo.Utc;

        public static TimeZoneInfo Resolve(string id, Logger log)
        {
            if (string.IsNullOrEmpty(id)) return _local;
            if (id == "UTC" || id == "Etc/UTC" || id == "Etc/GMT" || id == "Z") return Utc;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { }
            string win;
            if (IanaToWindows.TryGetValue(id, out win))
            {
                // значение может содержать несколько кандидатов через запятую
                // (идентификаторы зон менялись в обновлениях Windows)
                foreach (var candidate in win.Split(','))
                {
                    try { return TimeZoneInfo.FindSystemTimeZoneById(candidate.Trim()); }
                    catch { }
                }
            }
            if (log != null) log.Error("Неизвестный часовой пояс \"" + id + "\", использую локальный");
            return _local;
        }

        public static DateTimeOffset WallToInstant(DateTime wall, TimeZoneInfo tz)
        {
            if (tz.IsInvalidTime(wall)) wall = wall.AddHours(1); // переход на летнее время: грубая коррекция
            var off = tz.GetUtcOffset(wall);
            return new DateTimeOffset(wall, off);
        }

        static Dictionary<string, string> BuildMap()
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Россия / СНГ
            m["Europe/Moscow"] = "Russian Standard Time";
            m["Europe/Samara"] = "Samara Standard Time,Russia Time Zone 3";
            m["Europe/Kaliningrad"] = "Kaliningrad Standard Time";
            m["Europe/Kirov"] = "Russian Standard Time";
            m["Europe/Volgograd"] = "Volgograd Standard Time";
            m["Europe/Astrakhan"] = "Astrakhan Standard Time";
            m["Europe/Saratov"] = "Saratov Standard Time";
            m["Europe/Ulyanovsk"] = "Astrakhan Standard Time";
            m["Europe/Minsk"] = "Belarus Standard Time";
            m["Europe/Kyiv"] = "FLE Standard Time";
            m["Europe/Kiev"] = "FLE Standard Time";
            m["Europe/Riga"] = "FLE Standard Time";
            m["Europe/Tallinn"] = "FLE Standard Time";
            m["Europe/Vilnius"] = "FLE Standard Time";
            m["Europe/Chisinau"] = "E. Europe Standard Time";
            m["Asia/Tbilisi"] = "Georgian Standard Time";
            m["Asia/Yerevan"] = "Caucasus Standard Time";
            m["Asia/Baku"] = "Azerbaijan Standard Time";
            m["Asia/Almaty"] = "Central Asia Standard Time";
            m["Asia/Tashkent"] = "Tashkent Standard Time";
            m["Asia/Bishkek"] = "Central Asia Standard Time";
            m["Asia/Dushanbe"] = "West Asia Standard Time";
            m["Asia/Ashgabat"] = "West Asia Standard Time";
            m["Asia/Yekaterinburg"] = "Yekaterinburg Standard Time";
            m["Asia/Omsk"] = "Omsk Standard Time";
            m["Asia/Novosibirsk"] = "N. Central Asia Standard Time";
            m["Asia/Novokuznetsk"] = "N. Central Asia Standard Time";
            m["Asia/Krasnoyarsk"] = "North Asia Standard Time";
            m["Asia/Irkutsk"] = "North Asia East Standard Time";
            m["Asia/Yakutsk"] = "Yakutsk Standard Time";
            m["Asia/Vladivostok"] = "Vladivostok Standard Time";
            m["Asia/Magadan"] = "Magadan Standard Time";
            m["Asia/Sakhalin"] = "Sakhalin Standard Time";
            m["Asia/Kamchatka"] = "Kamchatka Standard Time";
            // Европа
            m["Europe/London"] = "GMT Standard Time";
            m["Europe/Dublin"] = "GMT Standard Time";
            m["Europe/Lisbon"] = "GMT Standard Time";
            m["Europe/Paris"] = "W. Europe Standard Time";
            m["Europe/Berlin"] = "W. Europe Standard Time";
            m["Europe/Madrid"] = "W. Europe Standard Time";
            m["Europe/Rome"] = "W. Europe Standard Time";
            m["Europe/Warsaw"] = "Central European Standard Time";
            m["Europe/Prague"] = "Central European Standard Time";
            m["Europe/Budapest"] = "Central Europe Standard Time";
            m["Europe/Vienna"] = "W. Europe Standard Time";
            m["Europe/Amsterdam"] = "W. Europe Standard Time";
            m["Europe/Brussels"] = "Romance Standard Time";
            m["Europe/Zurich"] = "W. Europe Standard Time";
            m["Europe/Stockholm"] = "W. Europe Standard Time";
            m["Europe/Oslo"] = "W. Europe Standard Time";
            m["Europe/Copenhagen"] = "Romance Standard Time";
            m["Europe/Athens"] = "GTB Standard Time";
            m["Europe/Helsinki"] = "FLE Standard Time";
            m["Europe/Sofia"] = "FLE Standard Time";
            m["Europe/Bucharest"] = "E. Europe Standard Time";
            m["Europe/Belgrade"] = "Central Europe Standard Time";
            m["Europe/Zagreb"] = "Central Europe Standard Time";
            m["Europe/Istanbul"] = "Turkey Standard Time";
            // Америка
            m["America/New_York"] = "Eastern Standard Time";
            m["America/Toronto"] = "Eastern Standard Time";
            m["America/Chicago"] = "Central Standard Time";
            m["America/Denver"] = "Mountain Standard Time";
            m["America/Los_Angeles"] = "Pacific Standard Time";
            m["America/Vancouver"] = "Pacific Standard Time";
            m["America/Phoenix"] = "US Mountain Standard Time";
            m["America/Anchorage"] = "Alaskan Standard Time";
            m["Pacific/Honolulu"] = "Hawaiian Standard Time";
            m["America/Sao_Paulo"] = "E. South America Standard Time";
            m["America/Argentina/Buenos_Aires"] = "Argentina Standard Time";
            m["America/Mexico_City"] = "Central Standard Time (Mexico)";
            m["America/Bogota"] = "SA Pacific Standard Time";
            m["America/Lima"] = "SA Pacific Standard Time";
            m["America/Santiago"] = "Pacific SA Standard Time";
            // Азия / Африка / Океания
            m["Asia/Dubai"] = "Arabian Standard Time";
            m["Asia/Tehran"] = "Iran Standard Time";
            m["Asia/Jerusalem"] = "Israel Standard Time";
            m["Asia/Beirut"] = "Middle East Standard Time";
            m["Asia/Amman"] = "Jordan Standard Time";
            m["Asia/Karachi"] = "Pakistan Standard Time";
            m["Asia/Kolkata"] = "India Standard Time";
            m["Asia/Calcutta"] = "India Standard Time";
            m["Asia/Kathmandu"] = "Nepal Standard Time";
            m["Asia/Dhaka"] = "Bangladesh Standard Time";
            m["Asia/Bangkok"] = "SE Asia Standard Time";
            m["Asia/Jakarta"] = "SE Asia Standard Time";
            m["Asia/Ho_Chi_Minh"] = "SE Asia Standard Time";
            m["Asia/Shanghai"] = "China Standard Time";
            m["Asia/Hong_Kong"] = "China Standard Time";
            m["Asia/Taipei"] = "Taipei Standard Time";
            m["Asia/Singapore"] = "Singapore Standard Time";
            m["Asia/Kuala_Lumpur"] = "Malay Peninsula Standard Time";
            m["Asia/Manila"] = "Singapore Standard Time";
            m["Asia/Seoul"] = "Korea Standard Time";
            m["Asia/Tokyo"] = "Tokyo Standard Time";
            m["Africa/Cairo"] = "Egypt Standard Time";
            m["Africa/Johannesburg"] = "South Africa Standard Time";
            m["Africa/Lagos"] = "W. Central Africa Standard Time";
            m["Africa/Nairobi"] = "E. Africa Standard Time";
            m["Africa/Casablanca"] = "Morocco Standard Time";
            m["Australia/Sydney"] = "AUS Eastern Standard Time";
            m["Australia/Melbourne"] = "AUS Eastern Standard Time";
            m["Australia/Brisbane"] = "E. Australia Standard Time";
            m["Australia/Perth"] = "W. Australia Standard Time";
            m["Australia/Adelaide"] = "Cen. Australia Standard Time";
            m["Pacific/Auckland"] = "New Zealand Standard Time";
            m["Pacific/Fiji"] = "Fiji Standard Time";
            return m;
        }
    }

    // Разбор iCalendar (RFC 5545, широко трактуемый) + раскрытие RRULE.
    public static class Ics
    {
        // ---------- публичный вход ----------

        public static List<RawEvent> Parse(string text, Logger log)
        {
            var result = new List<RawEvent>();
            if (string.IsNullOrEmpty(text)) return result;
            var lines = Unfold(text);
            RawEvent cur = null;
            var tzCache = new Dictionary<string, TimeZoneInfo>();
            foreach (var line in lines)
            {
                string name, param, value;
                SplitProp(line, out name, out param, out value);
                var upper = name.ToUpperInvariant();
                switch (upper)
                {
                    case "BEGIN":
                        if (value.Trim().Equals("VEVENT", StringComparison.OrdinalIgnoreCase)) cur = new RawEvent();
                        break;
                    case "END":
                        if (value.Trim().Equals("VEVENT", StringComparison.OrdinalIgnoreCase) && cur != null)
                        {
                            if (!string.IsNullOrEmpty(cur.Uid) && cur.Start != DateTime.MinValue) result.Add(cur);
                            cur = null;
                        }
                        break;
                    case "UID": if (cur != null) cur.Uid = value.Trim(); break;
                    case "SUMMARY": if (cur != null) cur.Subject = Unescape(value); break;
                    case "LOCATION": if (cur != null) cur.Location = Unescape(value); break;
                    case "DESCRIPTION": if (cur != null) cur.Description = Unescape(value); break;
                    case "STATUS": if (cur != null) cur.Cancelled = value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); break;
                    case "DTSTART":
                        if (cur != null)
                        {
                            bool allDay; string tzid;
                            DateTime dt = ParseDateValue(value, param, tzCache, log, out allDay, out tzid);
                            cur.Start = dt; cur.AllDay = allDay; cur.Tz = tzid;
                            if (allDay) { cur.End = dt.AddDays(1); }
                        }
                        break;
                    case "DTEND":
                        if (cur != null)
                        {
                            bool allDay; string tzid;
                            DateTime dt = ParseDateValue(value, param, tzCache, log, out allDay, out tzid);
                            cur.End = allDay ? dt : dt; // DATE в DTEND — эксклюзив
                            if (!allDay && cur.Tz == null) cur.Tz = tzid;
                            if (allDay && cur.End <= cur.Start) cur.End = cur.Start.AddDays(1);
                        }
                        break;
                    case "DURATION":
                        if (cur != null && cur.End == DateTime.MinValue && cur.Start != DateTime.MinValue)
                            cur.End = cur.Start + ParseDuration(value);
                        break;
                    case "RRULE": if (cur != null) cur.Rrule = value.Trim(); break;
                    case "EXDATE":
                        if (cur != null) AddDateList(cur.ExDates, value, param, cur.AllDay, tzCache, log);
                        break;
                    case "RDATE":
                        if (cur != null) AddDateList(cur.RDates, value, param, cur.AllDay, tzCache, log);
                        break;
                    case "RECURRENCE-ID":
                        if (cur != null)
                        {
                            bool allDay; string tzid;
                            DateTime dt = ParseDateValue(value, param, tzCache, log, out allDay, out tzid);
                            cur.RecurrenceId = allDay ? dt.Date
                                : Tz.WallToInstant(dt, Tz.Resolve(tzid, log)).UtcDateTime;
                        }
                        break;
                }
            }
            return result;
        }

        // Раскрывает событие (мастер или одиночное) в экземпляры в пределах окна.
        // horizonWall — граница в «настенном» времени пояса события.
        public static List<DateTime> ExpandInstances(RawEvent ev, DateTime horizonWall)
        {
            var outList = new List<DateTime>();
            if (ev.Rrule == null)
            {
                outList.Add(ev.Start);
                if (ev.RDates != null) outList.AddRange(ev.RDates.Count > 0 ? ConvertToWall(ev, ev.RDates) : new List<DateTime>());
            }
            else
            {
                outList.AddRange(ExpandRrule(ev, horizonWall));
                if (ev.RDates != null && ev.RDates.Count > 0) outList.AddRange(ConvertToWall(ev, ev.RDates));
            }
            // фильтр EXDATE
            if (ev.ExDates != null && ev.ExDates.Count > 0)
            {
                var excluded = new HashSet<long>();
                foreach (var ex in ev.ExDates)
                    excluded.Add(ev.AllDay ? ex.Date.Ticks : ex.Ticks); // instants уже UTC-тики
                var filtered = new List<DateTime>();
                foreach (var t in outList)
                {
                    long key;
                    if (ev.AllDay) key = t.Date.Ticks;
                    else key = Tz.WallToInstant(t, Tz.Resolve(ev.Tz, null)).UtcDateTime.Ticks;
                    if (!excluded.Contains(key)) filtered.Add(t);
                }
                outList = filtered;
            }
            // окно
            var win = new List<DateTime>();
            foreach (var t in outList) if (t <= horizonWall) win.Add(t);
            return win;
        }

        // ---------- разбор строк ----------

        static List<string> Unfold(string text)
        {
            var res = new List<string>();
            var normalized = text.Replace("\r\n", "\n");
            var arr = normalized.Split('\n');
            for (int i = 0; i < arr.Length; i++)
            {
                var line = arr[i];
                if (line.Length == 0) continue;
                // unfold: продолжение начинается с пробела/табуляции
                while (i + 1 < arr.Length && arr[i + 1].Length > 0 && (arr[i + 1][0] == ' ' || arr[i + 1][0] == '\t'))
                {
                    line += arr[i + 1].Substring(1);
                    i++;
                }
                res.Add(line.TrimEnd('\r'));
            }
            return res;
        }

        static void SplitProp(string line, out string name, out string param, out string value)
        {
            int colon = IndexOutsideQuotes(line, ':');
            if (colon < 0) { name = line; param = ""; value = ""; return; }
            value = line.Substring(colon + 1);
            var head = line.Substring(0, colon);
            int semi = IndexOutsideQuotes(head, ';');
            if (semi < 0) { name = head; param = ""; }
            else { name = head.Substring(0, semi); param = head.Substring(semi + 1); }
        }

        static int IndexOutsideQuotes(string s, char c)
        {
            bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '"') q = !q;
                else if (s[i] == c && !q) return i;
            }
            return -1;
        }

        public static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': case 'N': sb.Append('\n'); break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        static string GetParam(string param, string key)
        {
            if (string.IsNullOrEmpty(param)) return null;
            foreach (var part in param.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (part.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(eq + 1).Trim('"');
            }
            return null;
        }

        static DateTime ParseDateValue(string value, string param, Dictionary<string, TimeZoneInfo> tzCache, Logger log, out bool allDay, out string tzid)
        {
            allDay = false; tzid = "";
            value = value.Trim();
            bool isUtc = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            if (isUtc) value = value.Substring(0, value.Length - 1);
            var tzParam = GetParam(param, "TZID");
            if (tzParam != null) tzid = tzParam;
            else if (isUtc) tzid = "UTC";

            if (value.Length == 8 && value.IndexOf('T') < 0)
            {
                allDay = true;
                return ParseYmd(value);
            }
            var d = ParseYmdHms(value);
            if (d == DateTime.MinValue) return d;
            if (isUtc) { tzid = "UTC"; d = DateTime.SpecifyKind(d, DateTimeKind.Utc); }
            return d;
        }

        static DateTime ParseYmd(string s)
        {
            int y, mo, d;
            if (s.Length != 8 || !int.TryParse(s.Substring(0, 4), out y) || !int.TryParse(s.Substring(4, 2), out mo) || !int.TryParse(s.Substring(6, 2), out d)) return DateTime.MinValue;
            try { return new DateTime(y, mo, d); } catch { return DateTime.MinValue; }
        }

        static DateTime ParseYmdHms(string s)
        {
            var date = ParseYmd(s.Length > 8 ? s.Substring(0, 8) : s);
            if (date == DateTime.MinValue || s.Length < 15) return date;
            int h, mi, se;
            if (!int.TryParse(s.Substring(9, 2), out h) || !int.TryParse(s.Substring(11, 2), out mi) || !int.TryParse(s.Substring(13, 2), out se)) return date;
            try { return date.AddHours(h).AddMinutes(mi).AddSeconds(se); } catch { return date; }
        }

        static TimeSpan ParseDuration(string s)
        {
            // PnDTnHnMnS / PnW / PnD
            if (string.IsNullOrEmpty(s) || s[0] != 'P') return TimeSpan.Zero;
            s = s.Substring(1);
            bool neg = s.StartsWith("-"); if (neg) s = s.Substring(1);
            int days = 0; var time = TimeSpan.Zero;
            bool inTime = false; var num = new StringBuilder();
            foreach (var ch in s)
            {
                if (char.IsDigit(ch)) { num.Append(ch); continue; }
                int n = num.Length > 0 ? int.Parse(num.ToString()) : 0;
                num.Length = 0;
                switch (char.ToUpperInvariant(ch))
                {
                    case 'T': inTime = true; break;
                    case 'W': days += n * 7; break;
                    case 'D': days += n; break;
                    case 'H': if (inTime) time += TimeSpan.FromHours(n); break;
                    case 'M': if (inTime) time += TimeSpan.FromMinutes(n); break;
                    case 'S': if (inTime) time += TimeSpan.FromSeconds(n); break;
                }
            }
            var total = TimeSpan.FromDays(days) + time;
            return neg ? -total : total;
        }

        static void AddDateList(List<DateTime> target, string value, string param, bool allDay, Dictionary<string, TimeZoneInfo> tzCache, Logger log)
        {
            var tzParam = GetParam(param, "TZID");
            var tz = Tz.Resolve(tzParam, log);
            foreach (var v in value.Split(','))
            {
                var item = v.Trim();
                if (item.Length == 0) continue;
                bool a; string t;
                var dt = ParseDateValue(item, tzParam != null ? "TZID=" + tzParam : "", tzCache, log, out a, out t);
                if (dt == DateTime.MinValue) continue;
                if (allDay || a) target.Add(dt.Date);
                else
                {
                    if (t == "UTC") target.Add(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
                    else target.Add(Tz.WallToInstant(dt, tz).UtcDateTime);
                }
            }
        }

        static List<DateTime> ConvertToWall(RawEvent ev, List<DateTime> instants)
        {
            var res = new List<DateTime>();
            var tz = Tz.Resolve(ev.Tz, null);
            foreach (var i in instants)
            {
                if (ev.AllDay) res.Add(i.Date);
                else res.Add(TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(i, DateTimeKind.Utc), tz));
            }
            return res;
        }

        // ---------- RRULE ----------

        static List<DateTime> ExpandRrule(RawEvent ev, DateTime horizonWall)
        {
            var res = new List<DateTime>();
            var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ev.Rrule.Split(';'))
            {
                int eq = kv.IndexOf('=');
                if (eq > 0) parts[kv.Substring(0, eq)] = kv.Substring(eq + 1);
            }
            string freq;
            if (!parts.TryGetValue("FREQ", out freq)) return res;
            freq = freq.ToUpperInvariant();
            int interval = 1;
            if (parts.ContainsKey("INTERVAL")) int.TryParse(parts["INTERVAL"], out interval);
            if (interval < 1) interval = 1;
            int count = -1;
            if (parts.ContainsKey("COUNT")) int.TryParse(parts["COUNT"], out count);
            DateTime until = DateTime.MaxValue;
            if (parts.ContainsKey("UNTIL"))
            {
                bool a; string t;
                var d = ParseDateValue(parts["UNTIL"], "", new Dictionary<string, TimeZoneInfo>(), null, out a, out t);
                // UNTIL в UTC (обычно с Z) — сравниваем в настенном времени пояса события
                if (t == "UTC" && !ev.AllDay)
                    until = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(d, DateTimeKind.Utc), Tz.Resolve(ev.Tz, null));
                else until = d;
            }

            var byday = GetStringList(parts, "BYDAY");
            var bymonthday = GetList(parts, "BYMONTHDAY");
            var bymonth = GetList(parts, "BYMONTH");

            if (ev.AllDay)
            {
                // работаем в датах
                var bydayWeek = ParseByDayWeekdays(byday);
                var bydayOrd = ParseByDayOrdinal(byday);
                var dt = ev.Start.Date;
                int made = 0;
                if (freq == "DAILY")
                {
                    while (dt <= horizonWall && !HitLimit(made, count, res.Count))
                    {
                        if (bydayWeek == null || bydayWeek.Contains((int)dt.DayOfWeek)) Emit(res, dt, count, ref made);
                        dt = dt.AddDays(interval);
                    }
                }
                else if (freq == "WEEKLY")
                {
                    var days = bydayWeek ?? new HashSet<int> { (int)ev.Start.DayOfWeek };
                    var weekStart = StartOfWeek(dt);
                    while (weekStart <= horizonWall && !HitLimit(made, count, res.Count))
                    {
                        foreach (var dn in new[] { 1, 2, 3, 4, 5, 6, 0 }) // Пн..Вс
                        {
                            if (!days.Contains(dn)) continue;
                            var cand = weekStart.AddDays(dn - 1);
                            if (cand < ev.Start.Date) continue;
                            if (cand > horizonWall) break;
                            Emit(res, cand, count, ref made);
                        }
                        weekStart = weekStart.AddDays(7 * interval);
                    }
                }
                else if (freq == "MONTHLY")
                {
                    int y = dt.Year, mo = dt.Month;
                    while (new DateTime(y, mo, 1) <= horizonWall && !HitLimit(made, count, res.Count))
                    {
                        foreach (var cand in MonthCandidates(y, mo, bymonthday, bydayOrd, ev.Start))
                        {
                            if (cand < ev.Start.Date) continue;
                            if (cand > horizonWall) break;
                            Emit(res, cand, count, ref made);
                        }
                        mo += interval; y += (mo - 1) / 12; mo = (mo - 1) % 12 + 1;
                    }
                }
                else if (freq == "YEARLY")
                {
                    int y = dt.Year;
                    var months = bymonth != null && bymonth.Count > 0 ? bymonth : new List<int> { ev.Start.Month };
                    while (new DateTime(y, 1, 1) <= horizonWall && !HitLimit(made, count, res.Count))
                    {
                        foreach (var m in months)
                        {
                            foreach (var cand in MonthCandidates(y, m, bymonthday.Count > 0 ? bymonthday : new List<int> { ev.Start.Day }, bydayOrd, ev.Start))
                            {
                                if (cand < ev.Start.Date) continue;
                                if (cand > horizonWall) break;
                                Emit(res, cand, count, ref made);
                            }
                        }
                        y += interval;
                    }
                }
                else
                {
                    Emit(res, dt, count, ref made); // непонятная частота — хотя бы исходное
                }
                // ограничитель размера
                if (res.Count > 500) res = res.GetRange(0, 500);
                return res;
            }

            // события со временем
            var bydayWeekT = ParseByDayWeekdays(byday);
            var bydayOrdT = ParseByDayOrdinal(byday);
            var cur2 = ev.Start;
            int made2 = 0;
            if (freq == "DAILY")
            {
                while (cur2 <= horizonWall && !HitLimit(made2, count, res.Count))
                {
                    if (bydayWeekT == null || bydayWeekT.Contains((int)cur2.DayOfWeek)) Emit(res, cur2, count, ref made2);
                    cur2 = cur2.AddDays(interval);
                }
            }
            else if (freq == "WEEKLY")
            {
                var days = bydayWeekT ?? new HashSet<int> { (int)ev.Start.DayOfWeek };
                var weekStart = StartOfWeek(cur2);
                while (weekStart <= horizonWall && !HitLimit(made2, count, res.Count))
                {
                    foreach (var dn in new[] { 1, 2, 3, 4, 5, 6, 0 })
                    {
                        if (!days.Contains(dn)) continue;
                        var cand = weekStart.AddDays(dn - 1).Add(cur2.TimeOfDay);
                        if (cand < ev.Start) continue;
                        if (cand > horizonWall) break;
                        Emit(res, cand, count, ref made2);
                    }
                    weekStart = weekStart.AddDays(7 * interval);
                }
            }
            else if (freq == "MONTHLY")
            {
                int y = cur2.Year, mo = cur2.Month;
                while (new DateTime(y, mo, 1) <= horizonWall && !HitLimit(made2, count, res.Count))
                {
                    foreach (var cand in MonthCandidates(y, mo, bymonthday, bydayOrdT, ev.Start))
                    {
                        var c = cand == DateTime.MinValue ? DateTime.MinValue : cand.Date + cur2.TimeOfDay;
                        if (c < ev.Start) continue;
                        if (c > horizonWall) break;
                        Emit(res, c, count, ref made2);
                    }
                    mo += interval; y += (mo - 1) / 12; mo = (mo - 1) % 12 + 1;
                }
            }
            else if (freq == "YEARLY")
            {
                int y = cur2.Year;
                var months = bymonth != null && bymonth.Count > 0 ? bymonth : new List<int> { ev.Start.Month };
                while (new DateTime(y, 1, 1) <= horizonWall && !HitLimit(made2, count, res.Count))
                {
                    foreach (var m in months)
                    {
                        foreach (var cand in MonthCandidates(y, m, bymonthday.Count > 0 ? bymonthday : new List<int> { ev.Start.Day }, bydayOrdT, ev.Start))
                        {
                            var c = cand == DateTime.MinValue ? DateTime.MinValue : cand.Date + cur2.TimeOfDay;
                            if (c < ev.Start) continue;
                            if (c > horizonWall) break;
                            Emit(res, c, count, ref made2);
                        }
                    }
                    y += interval;
                }
            }
            else
            {
                Emit(res, cur2, count, ref made2);
            }
            if (res.Count > 500) res = res.GetRange(0, 500);
            return res;
        }

        static bool HitLimit(int made, int count, int produced)
        {
            if (count >= 0 && made >= count) return true;
            if (produced > 1000) return true;
            return false;
        }

        static void Emit(List<DateTime> res, DateTime t, int count, ref int made)
        {
            if (count >= 0 && made >= count) return;
            res.Add(t);
            made++;
        }

        static DateTime StartOfWeek(DateTime d)
        {
            // WKST по умолчанию MO
            int diff = ((int)d.DayOfWeek + 6) % 7;
            return d.Date.AddDays(-diff);
        }

        static List<int> GetList(Dictionary<string, string> parts, string key)
        {
            string v;
            if (!parts.TryGetValue(key, out v) || string.IsNullOrEmpty(v)) return new List<int>();
            var res = new List<int>();
            foreach (var x in v.Split(','))
            {
                int n;
                if (int.TryParse(x.Trim(), out n)) res.Add(n);
            }
            return res;
        }

        static List<string> GetStringList(Dictionary<string, string> parts, string key)
        {
            string v;
            if (!parts.TryGetValue(key, out v) || string.IsNullOrEmpty(v)) return new List<string>();
            var res = new List<string>();
            foreach (var x in v.Split(',')) res.Add(x.Trim());
            return res;
        }

        static HashSet<int> ParseByDayWeekdays(List<string> byday)
        {
            if (byday == null || byday.Count == 0) return null;
            var set = new HashSet<int>();
            foreach (var d in byday)
            {
                var code = d.Trim();
                // отбрасываем порядковые префиксы (2TU, -1FR)
                while (code.Length > 0 && (char.IsDigit(code[0]) || code[0] == '-' || code[0] == '+')) code = code.Substring(1);
                int n = DayCode(code);
                if (n >= 0) set.Add(n);
            }
            return set.Count > 0 ? set : null;
        }

        static List<KeyValuePair<int, int>> ParseByDayOrdinal(List<string> byday)
        {
            // "2TU" → (2, Вт); "-1FR" → (-1, Пт)
            var res = new List<KeyValuePair<int, int>>();
            if (byday == null) return res;
            foreach (var d in byday)
            {
                var code = d.Trim();
                int ord = 0, i = 0;
                bool hasOrd = false;
                if (code.Length > 0 && (char.IsDigit(code[0]) || code[0] == '-' || code[0] == '+'))
                {
                    hasOrd = true;
                    int sign = 1;
                    if (code[0] == '-') { sign = -1; i++; }
                    else if (code[0] == '+') i++;
                    int start = i;
                    while (i < code.Length && char.IsDigit(code[i])) i++;
                    int v; if (int.TryParse(code.Substring(start, i - start), out v)) ord = sign * v;
                }
                int day = DayCode(code.Substring(i));
                if (day >= 0) res.Add(new KeyValuePair<int, int>(ord, day));
                else if (hasOrd) { /* мусор — игнор */ }
            }
            return res;
        }

        static int DayCode(string s)
        {
            switch ((s ?? "").ToUpperInvariant())
            {
                case "MO": return 1;
                case "TU": return 2;
                case "WE": return 3;
                case "TH": return 4;
                case "FR": return 5;
                case "SA": return 6;
                case "SU": return 0;
                default: return -1;
            }
        }

        static IEnumerable<DateTime> MonthCandidates(int y, int mo, List<int> bymonthday, List<KeyValuePair<int, int>> bydayOrd, DateTime template)
        {
            var res = new List<DateTime>();
            int dim = DateTime.DaysInMonth(y, mo);
            if (bydayOrd != null && bydayOrd.Count > 0)
            {
                foreach (var kv in bydayOrd)
                {
                    var d = NthWeekdayOfMonth(y, mo, kv.Key, kv.Value);
                    if (d != DateTime.MinValue) res.Add(d);
                }
                return res;
            }
            if (bymonthday != null && bymonthday.Count > 0)
            {
                foreach (var md in bymonthday)
                {
                    int day = md > 0 ? md : dim + 1 + md;
                    if (day >= 1 && day <= dim) res.Add(new DateTime(y, mo, day));
                }
                return res;
            }
            int dd = template.Day <= dim ? template.Day : dim;
            res.Add(new DateTime(y, mo, dd));
            return res;
        }

        static DateTime NthWeekdayOfMonth(int y, int mo, int ord, int dayCode)
        {
            var first = new DateTime(y, mo, 1);
            int firstDow = (int)first.DayOfWeek;
            int delta = (dayCode - firstDow + 7) % 7;
            DateTime cand;
            if (ord > 0) cand = first.AddDays(delta + (ord - 1) * 7);
            else
            {
                int dim = DateTime.DaysInMonth(y, mo);
                var last = new DateTime(y, mo, dim);
                int lastDow = (int)last.DayOfWeek;
                int back = (lastDow - dayCode + 7) % 7;
                cand = last.AddDays(-back - (-ord - 1) * 7);
            }
            if (cand.Month != mo) return DateTime.MinValue;
            return cand;
        }

        // ---------- загрузка из сети/файла ----------

        public static string Fetch(IcsSource src, Logger log)
        {
            if (!string.IsNullOrEmpty(src.url))
            {
                var url = src.url.Trim();
                if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "wincal-agenda/1.0";
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 30000;
                    if (!string.IsNullOrEmpty(src.user))
                        req.Credentials = new NetworkCredential(src.user, src.password ?? "");
                    var resp = (HttpWebResponse)req.GetResponse();
                    using (var rs = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        rs.CopyTo(ms);
                        var bytes = ms.ToArray();
                        return DecodeBytes(bytes, resp.CharacterSet);
                    }
                }
                // протокол не распознан — пробуем как файл
            }
            if (!string.IsNullOrEmpty(src.path))
            {
                return File.ReadAllText(src.path, Encoding.UTF8);
            }
            throw new Exception("у источника \"" + src.name + "\" не задан ни url, ни path");
        }

        static string DecodeBytes(byte[] bytes, string charset)
        {
            Encoding enc = null;
            try { if (!string.IsNullOrEmpty(charset)) enc = Encoding.GetEncoding(charset); } catch { }
            if (enc == null) enc = Encoding.UTF8;
            return enc.GetString(bytes);
        }
    }
}
