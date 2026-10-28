using System;
using System.Collections.Generic;
using System.Threading;
using Windows.ApplicationModel.Appointments;
using Windows.Foundation;

namespace SmokeTest
{
    internal class ConsoleLogger : WinCal.Logger
    {
        protected override void OnLine(string line) { Console.WriteLine(line); }
    }
    internal static class Rt
    {
        public static T Await<T>(IAsyncOperation<T> op)
        {
            using (var evt = new ManualResetEvent(false))
            {
                op.Completed = delegate { evt.Set(); };
                evt.WaitOne();
                if (op.Status == AsyncStatus.Completed) return op.GetResults();
                if (op.Status == AsyncStatus.Error) throw op.ErrorCode;
                throw new OperationCanceledException();
            }
        }

        public static void Await(IAsyncAction action)
        {
            using (var evt = new ManualResetEvent(false))
            {
                action.Completed = delegate { evt.Set(); };
                evt.WaitOne();
                if (action.Status == AsyncStatus.Error) throw action.ErrorCode;
                if (action.Status == AsyncStatus.Canceled) throw new OperationCanceledException();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                var store = Rt.Await(AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AppCalendarsReadWrite));

                if (args == null || args.Length == 0)
                {
                    Console.WriteLine("usage: smoke diag [apptId] | get <apptId> | getdel <apptId...> | delcal <calId...>");
                    return;
                }

                if (args[0] == "parse" && args.Length > 1)
                {
                    var text = System.IO.File.ReadAllText(args[1]);
                    var raws = WinCal.Ics.Parse(text, null);
                    Console.WriteLine("events: " + raws.Count);
                    foreach (var ev in raws)
                    {
                        Console.WriteLine("  uid=" + ev.Uid + " subj=" + ev.Subject +
                            " start=" + ev.Start + " end=" + ev.End + " tz=[" + ev.Tz + "] allday=" + ev.AllDay +
                            " rrule=" + (ev.Rrule ?? "-"));
                    }
                    return;
                }

                if (args[0] == "mk" && args.Length > 1)
                {
                    // mk <minutes> — создать событие с заданной длительностью, вернуть id
                    var cal0 = Rt.Await(store.CreateAppointmentCalendarAsync("durtest"));
                    var ap0 = new Appointment();
                    ap0.Subject = "durtest";
                    ap0.StartTime = DateTimeOffset.Now.Date.AddDays(3).AddHours(15);
                    ap0.Duration = TimeSpan.FromMinutes(int.Parse(args[1]));
                    Rt.Await(cal0.SaveAppointmentAsync(ap0));
                    Console.WriteLine("created " + ap0.LocalId);
                    return;
                }

                if (args[0] == "mk2")
                {
                    // повторить точную последовательность присвоений из SyncCalendar
                    var cal0 = Rt.Await(store.CreateAppointmentCalendarAsync("durtest2"));
                    var ap0 = new Appointment();
                    ap0.Subject = "mk2 test";
                    ap0.StartTime = new DateTimeOffset(DateTime.Now.Date.AddDays(4).AddHours(11), TimeSpan.FromHours(4));
                    ap0.AllDay = false;
                    ap0.Duration = TimeSpan.FromMinutes(120);
                    Rt.Await(cal0.SaveAppointmentAsync(ap0));
                    Console.WriteLine("created " + ap0.LocalId + " dur=" + ap0.Duration);
                    return;
                }

                if (args[0] == "expand" && args.Length > 1)
                {
                    var text = System.IO.File.ReadAllText(args[1]);
                    var raws = WinCal.Ics.Parse(text, null);
                    Console.WriteLine("raws: " + raws.Count);
                    foreach (var ev in raws)
                        Console.WriteLine("  raw uid=" + ev.Uid + " start=" + ev.Start + " end=" + ev.End +
                            " endkind=" + ev.End.Kind + " startkind=" + ev.Start.Kind + " dur=" + (ev.End - ev.Start).TotalMinutes + "min");
                    var items = WinCal.SyncEngine.ExpandToItems(raws, "x",
                        DateTime.Now.Date.AddDays(-7), DateTime.Now.Date.AddDays(31), -1, null);
                    Console.WriteLine("items: " + items.Count);
                    var byDur = new Dictionary<string, int>();
                    int shown = 0;
                    foreach (var it in items)
                    {
                        var dk = it.Duration.ToString();
                        int c;
                        byDur[dk] = byDur.TryGetValue(dk, out c) ? c + 1 : 1;
                        if (shown < 4) { Console.WriteLine("  " + it.Start + " dur=" + dk + " " + it.Subject); shown++; }
                    }
                    foreach (var kv in byDur) Console.WriteLine("  dur " + kv.Key + " x" + kv.Value);
                    return;
                }

                if (args[0] == "scan" && args.Length > 2)
                {
                    // перебор id событий c,9,<hex> в диапазоне: какие календари реально живы
                    int from = Convert.ToInt32(args[1], 16);
                    int to = Convert.ToInt32(args[2], 16);
                    var calCounts = new SortedDictionary<string, int>();
                    var calNames = new Dictionary<string, string>();
                    var samples = new Dictionary<string, string>();
                    for (int i = from; i <= to; i++)
                    {
                        var id = "c,9," + i.ToString("x");
                        try
                        {
                            var a = Rt.Await(store.GetAppointmentAsync(id));
                            var cal = a.CalendarId;
                            if (cal == null) continue;
                            if (!calCounts.ContainsKey(cal)) { calCounts[cal] = 0; calNames[cal] = "?"; samples[cal] = id + " " + a.StartTime + " " + a.Subject; }
                            calCounts[cal]++;
                            try { var c = Rt.Await(store.GetAppointmentCalendarAsync(cal)); calNames[cal] = c.DisplayName; } catch { }
                        }
                        catch { }
                    }
                    foreach (var kv in calCounts)
                        Console.WriteLine("cal " + kv.Key + " [" + calNames[kv.Key] + "]: " + kv.Value + " appts; sample: " + samples[kv.Key]);
                    return;
                }

                if (args[0] == "tb")
                {
                    // чтение календарей Thunderbird с широким окном (диагностика)
                    var cfg = new WinCal.ThunderbirdConfig { enabled = true };
                    var tbs = WinCal.Thunderbird.Load(cfg,
                        DateTime.Now.Date.AddDays(-365), DateTime.Now.Date.AddDays(365),
                        new ConsoleLogger());
                    foreach (var tb in tbs)
                    {
                        Console.WriteLine("calendar [" + tb.Key + "] color=" + tb.Color + ": " + tb.Value.Count + " items /365d");
                        int shown = 0;
                        foreach (var it in tb.Value)
                        {
                            if (shown++ < 10)
                                Console.WriteLine("   " + it.Start + " dur=" + it.Duration + " allday=" + it.AllDay + " " + it.Subject);
                        }
                    }
                    return;
                }

                if (args[0] == "diag")
                {
                    Diag(store, args);
                    return;
                }

                if (args[0] == "get" && args.Length > 1)
                {
                    var a = Rt.Await(store.GetAppointmentAsync(args[1]));
                    Console.WriteLine("found: " + a.StartTime + " " + a.Subject + " cal=" + a.CalendarId +
                        " allday=" + a.AllDay + " dur=" + a.Duration + " busy=" + a.BusyStatus);
                    return;
                }

                if (args[0] == "getdel")
                {
                    // удалить календари, в которых лежат перечисленные события
                    var done = new HashSet<string>();
                    for (int i = 1; i < args.Length; i++)
                    {
                        try
                        {
                            var a = Rt.Await(store.GetAppointmentAsync(args[i]));
                            var cid = a.CalendarId;
                            if (cid == null || !done.Add(cid)) continue;
                            var c = Rt.Await(store.GetAppointmentCalendarAsync(cid));
                            Console.WriteLine("deleting calendar [" + c.DisplayName + "] " + cid);
                            Rt.Await(c.DeleteAsync());
                        }
                        catch (Exception ex) { Console.WriteLine(args[i] + ": " + ex.Message); }
                    }
                    return;
                }

                if (args[0] == "delcal")
                {
                    for (int i = 1; i < args.Length; i++)
                    {
                        try
                        {
                            var c = Rt.Await(store.GetAppointmentCalendarAsync(args[i]));
                            Console.WriteLine("deleting calendar [" + c.DisplayName + "] " + args[i]);
                            Rt.Await(c.DeleteAsync());
                        }
                        catch (Exception ex) { Console.WriteLine(args[i] + ": " + ex.Message); }
                    }
                    return;
                }

                Console.WriteLine("unknown command: " + args[0]);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex);
                Environment.ExitCode = 1;
            }
        }

        static void Diag(AppointmentStore store, string[] args)
        {
            Console.WriteLine("== calendars: default ==");
            var def = Rt.Await(store.FindAppointmentCalendarsAsync());
            Console.WriteLine("count=" + def.Count);
            foreach (var c in def) PrintCal(c);

            Console.WriteLine("== calendars: IncludeHidden ==");
            var all = Rt.Await(store.FindAppointmentCalendarsAsync(FindAppointmentCalendarsOptions.IncludeHidden));
            Console.WriteLine("count=" + all.Count);
            foreach (var c in all) PrintCal(c);

            Console.WriteLine("== store.FindAppointmentsAsync(now, 90d) ==");
            try
            {
                var appts = Rt.Await(store.FindAppointmentsAsync(DateTimeOffset.Now, new TimeSpan(90, 0, 0, 0)));
                Console.WriteLine("count=" + appts.Count);
                int shown = 0;
                foreach (var a in appts)
                {
                    if (shown++ < 5) Console.WriteLine("  id=" + a.LocalId + " cal=" + a.CalendarId + " " + a.StartTime + " " + a.Subject);
                }
            }
            catch (Exception ex) { Console.WriteLine("THROW: " + ex.Message); }

            if (args.Length > 1)
            {
                Console.WriteLine("== GetAppointmentAsync(" + args[1] + ") ==");
                try
                {
                    var a = Rt.Await(store.GetAppointmentAsync(args[1]));
                    Console.WriteLine("found: " + a.StartTime + " " + a.Subject + " cal=" + a.CalendarId);
                }
                catch (Exception ex) { Console.WriteLine("THROW: " + ex.Message); }
            }
        }

        static void PrintCal(AppointmentCalendar c)
        {
            int n;
            try { n = Rt.Await(c.FindAppointmentsAsync(DateTimeOffset.Now, new TimeSpan(90, 0, 0, 0))).Count; }
            catch (Exception ex) { Console.WriteLine("  enum-appts THROW: " + ex.Message); n = -1; }
            Console.WriteLine("  [" + c.DisplayName + "] id=" + c.LocalId + " hidden=" + c.IsHidden + " src=" + c.SourceDisplayName + " appts90d=" + n);
        }
    }
}
