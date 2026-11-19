using System.Globalization;
using TodoX.ReupVideo.Storage;

namespace TodoX.ReupVideo.Core;

public static class SchedulePlanner
{
    public const int MinIntervalMinutes = 10;

    /// <summary>"08:00, 12:30 20:00" → sorted unique times; invalid parts are reported in <paramref name="invalid"/>.</summary>
    public static List<TimeSpan> ParseTimes(IEnumerable<string> values, out List<string> invalid)
    {
        invalid = [];
        var times = new SortedSet<TimeSpan>();
        foreach (var raw in values.SelectMany(v => v.Split([',', ';', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)))
        {
            var text = raw.Trim();
            if (TimeSpan.TryParseExact(text, ["h\\:mm", "hh\\:mm", "h\\hmm", "hh\\hmm", "h\\h", "hh\\h"], CultureInfo.InvariantCulture, out var time)
                && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
            {
                times.Add(time);
            }
            else
            {
                invalid.Add(text);
            }
        }

        return times.ToList();
    }

    /// <summary>Next automatic run after <paramref name="now"/>; null in manual mode or with no valid times.</summary>
    public static DateTime? NextRun(ScheduleSettings schedule, DateTime now)
    {
        switch (schedule.Mode)
        {
            case ScheduleMode.DailyTimes:
            {
                var times = ParseTimes(schedule.DailyTimes, out _);
                if (times.Count == 0)
                {
                    return null;
                }

                // A slot that already ran (LastRunAt inside the same minute) is not run twice.
                foreach (var day in new[] { now.Date, now.Date.AddDays(1) })
                {
                    foreach (var time in times)
                    {
                        var slot = day + time;
                        if (slot > now || (slot > now.AddMinutes(-1) && (schedule.LastRunAt is null || schedule.LastRunAt < slot)))
                        {
                            return slot;
                        }
                    }
                }

                return now.Date.AddDays(1) + times[0];
            }
            case ScheduleMode.Interval:
            {
                var minutes = Math.Max(MinIntervalMinutes, schedule.IntervalMinutes);
                var last = schedule.LastRunAt;
                if (last is null || last > now)
                {
                    return now.AddMinutes(1);
                }

                var next = last.Value.AddMinutes(minutes);
                return next < now ? now : next;
            }
            default:
                return null;
        }
    }

    /// <summary>True when a run should start now (the scheduler ticks about every 20 seconds).</summary>
    public static bool IsDue(ScheduleSettings schedule, DateTime now)
    {
        if (schedule.Mode == ScheduleMode.Manual)
        {
            return false;
        }

        if (schedule.Mode == ScheduleMode.DailyTimes)
        {
            var times = ParseTimes(schedule.DailyTimes, out _);
            // Due when a slot passed within the last 2 minutes and it has not run since that slot.
            return times.Select(t => now.Date + t)
                .Any(slot => slot <= now && now - slot < TimeSpan.FromMinutes(2) && (schedule.LastRunAt is null || schedule.LastRunAt < slot));
        }

        var next = NextRun(schedule, now);
        return next is not null && next <= now;
    }
}
