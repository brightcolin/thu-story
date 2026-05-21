using System;
using System.Collections.Generic;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// 课表节次与签到规则。
    /// 可人工签到：[课前30分, 下课时刻]；其中 [课前30分, 开课] 为 on_time；(开课, 开课+30分] 为 late；(开课+30分, 下课] 为 absent。
    /// 早于课前 30 分为 too_early；strictly 晚于下课为 period_closed（勿提交 absent，仅提示）。
    /// </summary>
    public static class ClassPeriodAttendance
    {
        /// <summary>本节已结束，不可再手动签到（不提交 API）。</summary>
        public const string StatusPeriodClosed = "period_closed";

        /// <summary>下课后点击「上课」时的提示（与产品文案一致）。</summary>
        public const string UserHintPeriodClosed = "未到上课时间";

        /// <summary>第 1～4 节开课时刻（从 0 点起的分钟数）。</summary>
        static readonly int[] PeriodStartMin = { 0, 8 * 60 + 0, 9 * 60 + 50, 13 * 60 + 30, 19 * 60 + 20 };

        /// <summary>第 1～4 节下课时刻（从 0 点起的分钟数）。</summary>
        static readonly int[] PeriodEndMin = { 0, 9 * 60 + 30, 12 * 60 + 10, 15 * 60 + 0, 21 * 60 + 0 };

        public static int ClockToMinutes(int hour24, int minute) => hour24 * 60 + minute;

        /// <summary>
        /// 返回 on_time / late / absent（供 POST /class/attend）、too_early、或 period_closed（勿提交）。
        /// </summary>
        public static string Classify(int hour24, int minute, int period)
        {
            if (period < 1 || period > 4) return "absent";
            int now = ClockToMinutes(hour24, minute);
            int start = PeriodStartMin[period];
            int end = PeriodEndMin[period];
            if (now < start - 30) return "too_early";
            if (now > end) return StatusPeriodClosed;
            if (now <= start) return "on_time";
            if (now <= start + 30) return "late";
            return "absent";
        }

        /// <summary>已达或超过本节下课时刻（用于自动缺勤；含整点下课那一分钟）。</summary>
        public static bool IsPeriodEnded(int hour24, int minute, int period)
        {
            if (period < 1 || period > 4) return false;
            int now = ClockToMinutes(hour24, minute);
            return now >= PeriodEndMin[period];
        }

        public static int MinutesFromNowToPeriodEnd(int hour24, int minute, int period)
        {
            if (period < 1 || period > 4) return 0;
            int now = ClockToMinutes(hour24, minute);
            int end = PeriodEndMin[period];
            return Mathf.Max(0, end - now);
        }

        /// <summary>在今日课表中选取与当前时刻最匹配的一条排课，用于上课判定。</summary>
        public static bool TryPickSlotForCourseToday(
            string courseId,
            int weekday,
            ScheduleSlotV21[] schedule,
            int hour24,
            int minute,
            out ScheduleSlotV21 slot,
            out string apiAttendanceStatus,
            out string userHint)
        {
            slot = null;
            apiAttendanceStatus = null;
            userHint = null;

            if (string.IsNullOrEmpty(courseId))
            {
                userHint = "课程 ID 为空";
                return false;
            }

            if (weekday < 0 || weekday > 4)
            {
                userHint = "周末无排课";
                return false;
            }

            if (schedule == null || schedule.Length == 0)
            {
                userHint = "课表为空";
                return false;
            }

            var candidates = new List<ScheduleSlotV21>();
            foreach (var s in schedule)
            {
                if (s == null || string.IsNullOrEmpty(s.course_id)) continue;
                if (s.course_id != courseId) continue;
                if (s.day_of_week != weekday) continue;
                candidates.Add(s);
            }

            if (candidates.Count == 0)
            {
                userHint = "今天没有该课程的排课";
                return false;
            }

            ScheduleSlotV21 best = null;
            string bestRaw = null;
            int bestPri = -1;
            bool anyTooEarly = false;
            bool anyPeriodClosed = false;

            foreach (var c in candidates)
            {
                int p = Mathf.Clamp(c.period, 1, 4);
                string st = Classify(hour24, minute, p);
                if (st == "too_early")
                {
                    anyTooEarly = true;
                    continue;
                }
                if (st == StatusPeriodClosed)
                {
                    anyPeriodClosed = true;
                    continue;
                }

                int pri = Priority(st);
                if (pri > bestPri)
                {
                    bestPri = pri;
                    best = c;
                    bestRaw = st;
                }
            }

            slot = best;
            if (best == null || string.IsNullOrEmpty(bestRaw))
            {
                apiAttendanceStatus = null;
                if (anyTooEarly)
                    userHint = "还没到上课时间（可在课前 30 分钟内签到上课）";
                else if (anyPeriodClosed)
                    userHint = UserHintPeriodClosed;
                else
                    userHint = "无法匹配可签到的节次";
                return false;
            }

            apiAttendanceStatus = bestRaw;
            return true;
        }

        /// <summary>
        /// 在今日课表中找出「当前时刻」下出勤优先级最高的一批节次，并对每个课程仅保留一条（用于教室一键签所有课）。
        /// 规则：与 <see cref="TryPickSlotForCourseToday"/> 相同的时间窗，在全部格子中取 Priority 最大者；同优先级可含多门不同课程。
        /// </summary>
        public static bool TryCollectCoursesForBestSlotToday(
            int weekday,
            ScheduleSlotV21[] schedule,
            int hour24,
            int minute,
            List<(string courseId, ScheduleSlotV21 slot, string apiAttendanceStatus)> outList,
            out string userHint)
        {
            userHint = null;
            if (outList == null)
            {
                userHint = "内部错误";
                return false;
            }

            outList.Clear();

            if (weekday < 0 || weekday > 4)
            {
                userHint = "周末无排课";
                return false;
            }

            if (schedule == null || schedule.Length == 0)
            {
                userHint = "课表为空";
                return false;
            }

            int bestPri = -1;
            var tier = new List<ScheduleSlotV21>();
            bool anyWeekdaySlot = false;
            bool anyTooEarly = false;
            bool anyPeriodClosed = false;

            foreach (var s in schedule)
            {
                if (s == null || string.IsNullOrEmpty(s.course_id)) continue;
                if (s.day_of_week != weekday) continue;
                anyWeekdaySlot = true;
                int p = Mathf.Clamp(s.period, 1, 4);
                string st = Classify(hour24, minute, p);
                if (st == "too_early")
                {
                    anyTooEarly = true;
                    continue;
                }
                if (st == StatusPeriodClosed)
                {
                    anyPeriodClosed = true;
                    continue;
                }

                int pri = Priority(st);
                if (pri > bestPri)
                {
                    bestPri = pri;
                    tier.Clear();
                    tier.Add(s);
                }
                else if (pri == bestPri)
                    tier.Add(s);
            }

            if (bestPri < 0 || tier.Count == 0)
            {
                if (!anyWeekdaySlot)
                    userHint = "今天课表上没有排课";
                else if (anyTooEarly)
                    userHint = "还没到上课时间（可在课前 30 分钟内签到上课）";
                else if (anyPeriodClosed)
                    userHint = UserHintPeriodClosed;
                else
                    userHint = "无法匹配可签到的节次";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in tier)
            {
                if (!seen.Add(s.course_id)) continue;
                int p = Mathf.Clamp(s.period, 1, 4);
                string st = Classify(hour24, minute, p);
                outList.Add((s.course_id, s, st));
            }

            return outList.Count > 0;
        }

        /// <summary>数值越大越优先（用于同一天多节同名课）。</summary>
        static int Priority(string raw)
        {
            return raw switch
            {
                "on_time" => 4,
                "late" => 3,
                "absent" => 2,
                "too_early" => 1,
                StatusPeriodClosed => 0,
                _ => 0
            };
        }
    }
}
