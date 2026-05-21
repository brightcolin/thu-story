using System;
using System.Collections.Generic;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// 本节×游戏日本课程是否已记过出勤（手动或自动缺勤），避免重复 POST /class/attend。
    /// </summary>
    public static class ClassPeriodAttendanceSession
    {
        static readonly HashSet<string> HandledKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>在 total_days_elapsed 未返回时用语义日拼接，避免整档存档共用一个 key。</summary>
        public static int ResolveDayId(TimeInfoV21 time)
        {
            if (time == null) return 0;
            if (time.total_days_elapsed > 0)
                return time.total_days_elapsed;
            int byCalendar = time.semester_index * 1_000_000 + Mathf.Max(0, time.day_in_semester);
            if (byCalendar > 0)
                return byCalendar;
            return Mathf.RoundToInt(time.total_game_minutes / 1440f);
        }

        public static string MakeKey(int dayId, int weekday, int period, string courseId)
        {
            if (string.IsNullOrEmpty(courseId)) return "";
            return $"{dayId}|{weekday}|{period}|{courseId}";
        }

        public static string MakeKey(TimeInfoV21 time, int weekday, int period, string courseId) =>
            MakeKey(ResolveDayId(time), weekday, period, courseId);

        public static bool WasHandled(string key) =>
            !string.IsNullOrEmpty(key) && HandledKeys.Contains(key);

        public static void RegisterManualAttend(TimeInfoV21 time, int weekday, int period, string courseId)
        {
            string k = MakeKey(time, weekday, period, courseId);
            if (!string.IsNullOrEmpty(k))
                HandledKeys.Add(k);
        }

        public static void RegisterManualAttend(int dayId, int weekday, int period, string courseId)
        {
            string k = MakeKey(dayId, weekday, period, courseId);
            if (!string.IsNullOrEmpty(k))
                HandledKeys.Add(k);
        }

        public static void RegisterAutoAbsentSent(TimeInfoV21 time, int weekday, int period, string courseId)
        {
            string k = MakeKey(time, weekday, period, courseId);
            if (!string.IsNullOrEmpty(k))
                HandledKeys.Add(k);
        }

        public static void RegisterAutoAbsentSent(int dayId, int weekday, int period, string courseId)
        {
            string k = MakeKey(dayId, weekday, period, courseId);
            if (!string.IsNullOrEmpty(k))
                HandledKeys.Add(k);
        }
    }
}
