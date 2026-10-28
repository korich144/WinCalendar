using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Windows.ApplicationModel.Appointments;

namespace WinCal
{
    // Синхронизация списка EventItem в календарь системного AppointmentStore.
    public class SyncEngine
    {
        readonly AppConfig cfg;
        readonly Logger log;
        Dictionary<string, Dictionary<string, string>> state;

        // Календари, оставшиеся от прототипов на этой машине — чистим при первом запуске.
        static readonly string[] LegacyTestCalendars = { "ICS Import (test)", "csc-smoke" };

        public SyncEngine(AppConfig cfg, Logger log)
        {
            this.cfg = cfg;
            this.log = log;
            state = ConfigStore.LoadState();
        }

        public string SyncAll()
        {
            state = ConfigStore.LoadState();
            var store = Rt.Await(AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite));

            var now = DateTime.Now;
            var windowStart = now.Date.AddDays(-cfg.daysBackward);
            var windowEnd = now.Date.AddDays(cfg.daysForward + 1);

            var wantedCalendars = new HashSet<string>();

            int total = 0;
            var problems = new List<string>();

            // 1. Прототипные календари — удалить
            foreach (var cal in Rt.Await(store.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden)))
            {
                foreach (var legacy in LegacyTestCalendars)
                {
                    if (string.Equals(cal.DisplayName, legacy, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Info("Удаляю тестовый календарь: " + legacy);
                        Rt.Await(cal.DeleteAsync());
                        state.Remove(legacy);
                        state.Remove("__cal:" + legacy);
                    }
                }
            }

            // 2. ICS-источники
            foreach (var src in cfg.sources)
            {
                if (!src.enabled) continue;
                if (string.IsNullOrEmpty(src.name)) { problems.Add("источник без имени"); continue; }
                var calName = cfg.prefix + src.name;
                wantedCalendars.Add(calName);
                try
                {
                    var items = LoadIcsSource(src, windowStart, windowEnd);
                    var cal = EnsureCalendar(store, calName, src.color);
                    SyncCalendar(store, cal, items);
                    total += items.Count;
                }
                catch (Exception ex)
                {
                    var msg = "ICS \"" + src.name + "\": " + ex.Message;
                    problems.Add(msg);
                    log.Error(msg);
                }
            }

            // 3. Thunderbird
            if (cfg.thunderbird.enabled)
            {
                try
                {
                    var tbs = Thunderbird.Load(cfg.thunderbird, windowStart, windowEnd, log);
                    foreach (var tb in tbs)
                    {
                        if (tb.Value.Count == 0) continue; // пустой календарь не создаём
                        var calName = cfg.prefix + tb.Key;
                        wantedCalendars.Add(calName);
                        try
                        {
                            var cal = EnsureCalendar(store, calName, tb.Color);
                            SyncCalendar(store, cal, tb.Value);
                            total += tb.Value.Count;
                        }
                        catch (Exception ex)
                        {
                            var msg = "TB \"" + tb.Key + "\": " + ex.Message;
                            problems.Add(msg);
                            log.Error(msg);
                        }
                    }
                }
                catch (Exception ex)
                {
                    var msg = "Thunderbird: " + ex.Message;
                    problems.Add(msg);
                    log.Error(msg);
                }
            }

            // 4. Календари префикса, пропавшие из конфига — удалить
            if (!string.IsNullOrEmpty(cfg.prefix))
            {
                foreach (var cal in Rt.Await(store.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden)))
                {
                    if (cal.DisplayName != null && cal.DisplayName.StartsWith(cfg.prefix, StringComparison.Ordinal)
                        && !wantedCalendars.Contains(cal.DisplayName))
                    {
                        log.Info("Календарь \"" + cal.DisplayName + "\" пропал из конфига — удаляю");
                        Rt.Await(cal.DeleteAsync());
                        state.Remove(cal.DisplayName);
                        state.Remove("__cal:" + cal.DisplayName);
                    }
                }
            }

            // 5. Календари, оставшиеся в state, но исчезнувшие из конфига — удалить
            var staleStateKeys = new List<string>();
            foreach (var kv in state)
                if (!wantedCalendars.Contains(kv.Key)) staleStateKeys.Add(kv.Key);
            foreach (var staleName in staleStateKeys)
            {
                Dictionary<string, string> m;
                if (!state.TryGetValue(staleName, out m)) continue;
                string calId;
                if (m.TryGetValue("__calendarLocalId", out calId) && !string.IsNullOrEmpty(calId))
                {
                    try
                    {
                        var staleCal = Rt.Await(store.GetAppointmentCalendarAsync(calId));
                        log.Info("Календарь \"" + staleName + "\" больше не в конфиге — удаляю (" + calId + ")");
                        Rt.Await(staleCal.DeleteAsync());
                    }
                    catch { }
                }
                state.Remove(staleName);
            }

            ConfigStore.SaveState(state);
            var summary = "Событий в панели: " + total;
            if (problems.Count > 0) summary += "; проблем: " + problems.Count;
            log.Info("Синхронизация завершена. " + summary);
            return summary;
        }

        List<EventItem> LoadIcsSource(IcsSource src, DateTime windowStart, DateTime windowEnd)
        {
            var text = Ics.Fetch(src, log);
            var raws = Ics.Parse(text, log);
            return ExpandToItems(raws, cfg.prefix + src.name, windowStart, windowEnd, src.reminderMinutes, log);
        }

        // Мастера + переопределения (RECURRENCE-ID) → плоский список экземпляров в окне.
        public static List<EventItem> ExpandToItems(List<RawEvent> raws, string calendarName, DateTime windowStart, DateTime windowEnd, int reminderMinutes, Logger log)
        {
            var items = new List<EventItem>();
            var horizonWall = windowEnd;
            var masters = new Dictionary<string, List<RawEvent>>();
            var overrides = new List<RawEvent>();
            foreach (var ev in raws)
            {
                if (ev.Cancelled && !ev.IsOverride) continue;
                if (ev.IsOverride) overrides.Add(ev);
                else
                {
                    List<RawEvent> list;
                    if (!masters.TryGetValue(ev.Uid, out list)) { list = new List<RawEvent>(); masters[ev.Uid] = list; }
                    list.Add(ev);
                }
            }

            foreach (var kv in masters)
            {
                foreach (var master in kv.Value)
                {
                    var tz = Tz.Resolve(master.Tz, log);
                    // граница горизонта в настенном времени пояса события
                    var horizonWallUtc = new DateTimeOffset(DateTime.SpecifyKind(windowEnd, DateTimeKind.Unspecified), TimeSpan.Zero);
                    var horizon = TimeZoneInfo.ConvertTime(horizonWallUtc, tz).DateTime;
                    var instances = Ics.ExpandInstances(master, horizon);
                    foreach (var wall in instances)
                    {
                        var item = MakeItem(master, wall, tz, calendarName, reminderMinutes);
                        if (item != null) items.Add(item);
                    }
                    if (log != null && instances.Count > 0)
                        log.Info(" \"" + master.Subject + "\": экземпляров " + instances.Count);
                }
            }

            foreach (var ov in overrides)
            {
                if (ov.Cancelled) continue; // отменённый экземпляр просто не пишем
                var tz = Tz.Resolve(ov.Tz, log);
                var item = MakeItem(ov, ov.Start, tz, calendarName, reminderMinutes);
                if (item == null) continue;
                // заменить экземпляр мастера с тем же RecurrenceId
                if (ov.RecurrenceId != null)
                {
                    var ridTicks = ov.RecurrenceId.Value.Ticks;
                    items.RemoveAll(delegate(EventItem it) { return it.Key == ov.Uid + "#" + ridTicks; });
                }
                items.Add(item);
            }

            // окно
            var res = new List<EventItem>();
            foreach (var it in items)
            {
                var end = it.Start + (it.Duration == TimeSpan.Zero ? TimeSpan.FromMinutes(1) : it.Duration);
                if (end < windowStart) continue;
                if (it.Start > windowEnd) continue;
                res.Add(it);
            }
            return res;
        }

        static EventItem MakeItem(RawEvent ev, DateTime wall, TimeZoneInfo tz, string calendarName, int reminderMinutes)
        {
            var item = new EventItem();
            item.Subject = Clamp(ev.Subject ?? "(без названия)", 250);
            item.Location = Clamp(ev.Location, 250);
            item.Description = Clamp(ev.Description, 1800);
            if (ev.AllDay)
            {
                var localMidnight = new DateTime(wall.Year, wall.Month, wall.Day, 0, 0, 0, DateTimeKind.Local);
                item.AllDay = true;
                item.Start = new DateTimeOffset(localMidnight);
                var days = (int)(ev.End.Date - ev.Start.Date).TotalDays;
                if (days < 1) days = 1;
                item.Duration = TimeSpan.FromDays(days);
                item.Key = ev.Uid + "#" + wall.Date.Ticks;
            }
            else
            {
                item.Start = Tz.WallToInstant(wall, tz);
                // длительность берём у мастера (End - Start), она одинакова для всех экземпляров
                item.Duration = ev.End == DateTime.MinValue ? TimeSpan.Zero : ev.End - ev.Start;
                if (item.Duration < TimeSpan.Zero) item.Duration = TimeSpan.Zero;
                var instTicks = item.Start.UtcTicks;
                item.Key = ev.Uid + "#" + instTicks;
            }
            item.ReminderMinutes = reminderMinutes;
            return item;
        }

        static string Clamp(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        AppointmentCalendar EnsureCalendar(AppointmentStore store, string name, string colorHex)
        {
            AppointmentCalendar cal = null;
            var calState = GetCalState(name);
            string savedId;
            if (!calState.TryGetValue("__calendarLocalId", out savedId)) savedId = null;
            if (savedId != null)
            {
                try { cal = Rt.Await(store.GetAppointmentCalendarAsync(savedId)); }
                catch { }
            }
            if (cal == null)
            {
                var candidates = new List<AppointmentCalendar>();
                CollectByName(store, name, candidates);
                if (candidates.Count == 0) CollectByNameViaAppointments(store, name, candidates);
                if (candidates.Count > 0)
                {
                    cal = candidates[0];
                    for (int i = 1; i < candidates.Count; i++)
                    {
                        log.Info("Дубликат календаря \"" + name + "\" — удаляю лишний (" + candidates[i].LocalId + ")");
                        try { Rt.Await(candidates[i].DeleteAsync()); }
                        catch (Exception ex) { log.Error("Не удалось удалить дубликат: " + ex.Message); }
                    }
                }
            }
            if (cal == null)
            {
                log.Info("Создаю календарь: " + name);
                cal = Rt.Await(store.CreateAppointmentCalendarAsync(name));
            }
            calState["__calendarLocalId"] = cal.LocalId;
            var rgb = ParseColorBytes(colorHex);
            if (rgb != null) SetDisplayColor(cal, rgb);
            return cal;
        }

        // Календари становятся скрытыми для перечисления после выхода создавшего их
        // процесса, поэтому ищем и с IncludeHidden, и сканом по событиям (CalendarId).
        void CollectByName(AppointmentStore store, string name, List<AppointmentCalendar> into)
        {
            try
            {
                foreach (var c in Rt.Await(store.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden)))
                    if (string.Equals(c.DisplayName, name, StringComparison.Ordinal)) into.Add(c);
            }
            catch (Exception ex) { log.Error("Перечисление календарей: " + ex.Message); }
        }

        void CollectByNameViaAppointments(AppointmentStore store, string name, List<AppointmentCalendar> into)
        {
            try
            {
                var ids = new HashSet<string>();
                foreach (var a in StoreAppointments(store))
                {
                    var cid = a.CalendarId;
                    if (cid == null || !ids.Add(cid)) continue;
                    try
                    {
                        var c = Rt.Await(store.GetAppointmentCalendarAsync(cid));
                        if (string.Equals(c.DisplayName, name, StringComparison.Ordinal)) into.Add(c);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { log.Error("Сканирование событий: " + ex.Message); }
        }

        List<Appointment> StoreAppointments(AppointmentStore store)
        {
            var res = new List<Appointment>();
            var start = DateTimeOffset.Now.AddDays(-cfg.daysBackward - 1);
            var span = TimeSpan.FromDays(cfg.daysBackward + cfg.daysForward + 2);
            try
            {
                foreach (var a in Rt.Await(store.FindAppointmentsAsync(start, span))) res.Add(a);
            }
            catch
            {
                // некоторые сборки ограничивают длину интервала — идём кусками
                var s = start;
                for (int i = 0; i < 8; i++)
                {
                    foreach (var a in Rt.Await(store.FindAppointmentsAsync(s, TimeSpan.FromDays(180)))) res.Add(a);
                    s = s.AddDays(180);
                }
            }
            return res;
        }

        // Windows.UI.Color в проекции csc недоступен напрямую — ставим цвет через рефлексию.
        static void SetDisplayColor(AppointmentCalendar cal, byte[] rgb)
        {
            try
            {
                var prop = typeof(AppointmentCalendar).GetProperty("DisplayColor");
                if (prop == null || !prop.CanWrite) return;
                var t = prop.PropertyType;
                object val = Activator.CreateInstance(t);
                SetField(t, val, "A", 255);
                SetField(t, val, "R", rgb[0]);
                SetField(t, val, "G", rgb[1]);
                SetField(t, val, "B", rgb[2]);
                prop.SetValue(cal, val, null);
                Rt.Await(cal.SaveAsync());
            }
            catch (Exception)
            {
                // цвет не критичен
            }
        }

        static void SetField(Type t, object inst, string name, byte v)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(inst, v);
        }

        // Appointment.Reminder в проекции csc имеет тип TimeSpan? — ставим по фактическому типу свойства.
        static void SetReminder(Appointment a, int minutes)
        {
            try
            {
                var prop = typeof(Appointment).GetProperty("Reminder");
                if (prop == null || !prop.CanWrite) return;
                var pt = prop.PropertyType;
                if (pt == typeof(TimeSpan?)) prop.SetValue(a, TimeSpan.FromMinutes(minutes), null);
                else if (pt == typeof(int?)) prop.SetValue(a, minutes, null);
            }
            catch { }
        }

        // Дифф строится по state-карте (ключ события → localId|хеш) и точечным
        // GetAppointmentAsync: перечисление событий через API после выхода
        // создавшего их процесса не работает (см. исследование), а по id — работает.
        void SyncCalendar(AppointmentStore store, AppointmentCalendar cal, List<EventItem> items)
        {
            var map = GetCalState(cal.DisplayName);
            int added = 0, deleted = 0, updated = 0;
            var desiredKeys = new HashSet<string>();
            foreach (var it in items) desiredKeys.Add(it.Key);

            var staleKeys = new List<string>();
            foreach (var kv in map)
            {
                if (kv.Key == "__calendarLocalId") continue;
                if (!desiredKeys.Contains(kv.Key)) staleKeys.Add(kv.Key);
            }
            foreach (var key in staleKeys)
            {
                var parts2 = map[key].Split('|');
                if (parts2.Length == 2 && TryGetAppt(store, parts2[0]) != null)
                {
                    try { Rt.Await(cal.DeleteAppointmentAsync(parts2[0])); deleted++; }
                    catch (Exception ex) { log.Error("Удаление \"" + key + "\": " + ex.Message); }
                }
                map.Remove(key);
            }

            foreach (var it in items)
            {
                string stateVal;
                string localId = null, hash = null;
                if (map.TryGetValue(it.Key, out stateVal))
                {
                    var parts2 = stateVal.Split('|');
                    if (parts2.Length == 2) { localId = parts2[0]; hash = parts2[1]; }
                }
                Appointment existing = localId != null ? TryGetAppt(store, localId) : null;
                if (existing != null && hash == HashOf(it)) continue; // без изменений
                if (existing != null)
                {
                    try { Rt.Await(cal.DeleteAppointmentAsync(localId)); }
                    catch (Exception ex) { log.Error("Замена \"" + it.Key + "\": " + ex.Message); }
                    updated++;
                }
                var a = new Appointment();
                a.Subject = it.Subject;
                if (!string.IsNullOrEmpty(it.Description)) a.Details = it.Description;
                if (!string.IsNullOrEmpty(it.Location)) a.Location = it.Location;
                a.StartTime = it.Start;
                a.AllDay = it.AllDay;
                a.Duration = it.AllDay ? it.Duration : (it.Duration < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : it.Duration);
                if (it.ReminderMinutes >= 0) SetReminder(a, it.ReminderMinutes);
                Rt.Await(cal.SaveAppointmentAsync(a));
                map[it.Key] = a.LocalId + "|" + HashOf(it);
                if (existing == null) added++;
            }

            if (added > 0 || deleted > 0 || updated > 0)
                log.Info("Календарь \"" + cal.DisplayName + "\": +" + added + " / -" + deleted + " / ~" + updated);
        }

        Appointment TryGetAppt(AppointmentStore store, string localId)
        {
            try { return Rt.Await(store.GetAppointmentAsync(localId)); }
            catch { return null; }
        }

        Dictionary<string, string> GetCalState(string calName)
        {
            Dictionary<string, string> m;
            if (!state.TryGetValue(calName, out m)) { m = new Dictionary<string, string>(); state[calName] = m; }
            return m;
        }

        static List<Appointment> ToList(IReadOnlyList<Appointment> v)
        {
            var res = new List<Appointment>();
            foreach (var x in v) res.Add(x);
            return res;
        }

        static string HashOf(EventItem it)
        {
            var sb = new StringBuilder();
            sb.Append(it.Subject).Append('\x1');
            sb.Append(it.Location).Append('\x1');
            sb.Append(it.Description).Append('\x1');
            sb.Append(it.Start.UtcTicks).Append('\x1');
            sb.Append(it.Duration.Ticks).Append('\x1');
            sb.Append(it.AllDay).Append('\x1');
            sb.Append(it.ReminderMinutes);
            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(b.Length * 2);
                foreach (var x in b) hex.Append(x.ToString("x2"));
                return hex.ToString();
            }
        }

        static byte[] ParseColorBytes(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return null;
                var s = hex.TrimStart('#');
                if (s.Length == 6)
                {
                    var r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
                    var g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
                    var b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
                    return new byte[] { r, g, b };
                }
            }
            catch { }
            return null;
        }

        public void DeleteCalendar(string name)
        {
            var store = Rt.Await(AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite));
            AppointmentCalendar cal = null;
            string savedId;
            if (!state.ContainsKey(name) || !state[name].TryGetValue("__calendarLocalId", out savedId)) savedId = null;
            if (savedId != null)
            {
                try { cal = Rt.Await(store.GetAppointmentCalendarAsync(savedId)); } catch { }
            }
            if (cal == null)
            {
                var candidates = new List<AppointmentCalendar>();
                CollectByName(store, name, candidates);
                if (candidates.Count == 0) CollectByNameViaAppointments(store, name, candidates);
                if (candidates.Count > 0) cal = candidates[0];
            }
            if (cal == null) { log.Info("Календарь не найден: " + name); return; }
            Rt.Await(cal.DeleteAsync());
            state.Remove(name);
            ConfigStore.SaveState(state);
            log.Info("Календарь удалён: " + name);
        }
    }
}
