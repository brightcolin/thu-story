using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// v2.1：上课走 POST /class/attend。根据服务端时间与课表计算 on_time / late / absent，
    /// 请求体携带 attendance_status、day_of_week、period；成功后若未到本节下课，则 POST /time/advance?minutes=N 推进到下课时。
    /// </summary>
    public static class ServerAttendClassFlow
    {
        /// <param name="advanceTimeAfterSuccess">为 true 时，在出勤请求成功后尝试将时间推进到本节下课（与 playercontrol 一致）。</param>
        public static IEnumerator Run(string courseId, bool advanceTimeAfterSuccess = false,
            Action<AttendClassResponseV21, string> onComplete = null)
        {
            APIManager.EnsureExists();
            if (APIManager.Instance == null)
            {
                Debug.LogWarning("[ServerAttendClassFlow] APIManager 缺失");
                onComplete?.Invoke(null, "APIManager 未初始化");
                yield break;
            }

            if (string.IsNullOrEmpty(courseId))
            {
                onComplete?.Invoke(null, "courseId 为空");
                yield break;
            }

            TimeInfoV21 time = null;
            string timeErr = null;
            bool timeDone = false;
            APIManager.Instance.GetTimeV21(
                t => { time = t; timeDone = true; },
                e => { timeErr = e; timeDone = true; });
            while (!timeDone) yield return null;

            if (!string.IsNullOrEmpty(timeErr) || time == null)
            {
                onComplete?.Invoke(null, "无法获取游戏时间：" + timeErr);
                yield break;
            }

            ScheduleResponseV21 sched = null;
            string schErr = null;
            bool schDone = false;
            APIManager.Instance.GetScheduleV21(
                s => { sched = s; schDone = true; },
                e => { schErr = e; schDone = true; });
            while (!schDone) yield return null;

            if (!string.IsNullOrEmpty(schErr))
            {
                onComplete?.Invoke(null, "无法获取课表：" + schErr);
                yield break;
            }

            ScheduleSlotV21[] slotsForAttend = sched?.schedule;

            if (!ClassPeriodAttendance.TryPickSlotForCourseToday(
                    courseId, time.weekday, slotsForAttend, time.hour, time.minute,
                    out var slot, out var attendanceStatus, out var pickHint))
            {
                onComplete?.Invoke(null, pickHint ?? "无法匹配课表节次");
                yield break;
            }

            int period = Mathf.Clamp(slot.period, 1, 4);
            int dow = Mathf.Clamp(slot.day_of_week, 0, 4);

            bool done = false;
            AttendClassResponseV21 result = null;
            string err = null;
            APIManager.Instance.AttendClassV21(courseId, attendanceStatus, dow, period,
                r => { result = r; done = true; },
                e => { err = e; done = true; });
            while (!done) yield return null;

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning($"[ServerAttendClassFlow] {courseId}: {err}");
                onComplete?.Invoke(null, err);
                yield break;
            }

            if (result == null)
            {
                onComplete?.Invoke(null, "空响应");
                yield break;
            }

            if (result.success)
                ClassPeriodAttendanceSession.RegisterManualAttend(time, dow, period, courseId);

            PlayerManager.Instance?.ProcessAttendClassResultV21(result);

            if (result.success && advanceTimeAfterSuccess && APIManager.Instance != null)
            {
                TimeInfoV21 tAfter = null;
                bool t2d = false;
                APIManager.Instance.GetTimeV21(t => { tAfter = t; t2d = true; }, _ => t2d = true);
                while (!t2d) yield return null;

                if (tAfter != null)
                {
                    int rem = ClassPeriodAttendance.MinutesFromNowToPeriodEnd(tAfter.hour, tAfter.minute, period);
                    if (rem > 0)
                    {
                        bool advDone = false;
                        APIManager.Instance.AdvanceTimeMinutesV21(rem,
                            () => advDone = true,
                            e =>
                            {
                                if (!string.IsNullOrEmpty(e))
                                    Debug.LogWarning("[ServerAttendClassFlow] 推进到下课失败: " + e);
                                advDone = true;
                            });
                        while (!advDone) yield return null;
                    }
                }

                PlayerManager.Instance?.RefreshFromServer();
            }

            onComplete?.Invoke(result, null);
        }

        /// <summary>
        /// 按当前游戏时间与课表，对「当前节次」下所有已选课程依次 POST /class/attend；全部完成后再最多推进一次时间到本节下课。
        /// </summary>
        public static IEnumerator RunAllCoursesForCurrentPeriod(bool advanceTimeAfterSuccess,
            Action<List<AttendClassResponseV21>, string> onComplete = null)
        {
            APIManager.EnsureExists();
            var results = new List<AttendClassResponseV21>();

            if (APIManager.Instance == null)
            {
                Debug.LogWarning("[ServerAttendClassFlow] APIManager 缺失");
                onComplete?.Invoke(results, "APIManager 未初始化");
                yield break;
            }

            TimeInfoV21 time = null;
            string timeErr = null;
            bool timeDone = false;
            APIManager.Instance.GetTimeV21(
                t => { time = t; timeDone = true; },
                e => { timeErr = e; timeDone = true; });
            while (!timeDone) yield return null;

            if (!string.IsNullOrEmpty(timeErr) || time == null)
            {
                onComplete?.Invoke(results, "无法获取游戏时间：" + timeErr);
                yield break;
            }

            ScheduleResponseV21 sched = null;
            string schErr = null;
            bool schDone = false;
            APIManager.Instance.GetScheduleV21(
                s => { sched = s; schDone = true; },
                e => { schErr = e; schDone = true; });
            while (!schDone) yield return null;

            if (!string.IsNullOrEmpty(schErr))
            {
                onComplete?.Invoke(results, "无法获取课表：" + schErr);
                yield break;
            }

            ScheduleSlotV21[] slotsForAttend = sched?.schedule;

            var toAttend = new List<(string courseId, ScheduleSlotV21 slot, string attendanceStatus)>();
            if (!ClassPeriodAttendance.TryCollectCoursesForBestSlotToday(
                    time.weekday, slotsForAttend, time.hour, time.minute, toAttend, out var pickHint))
            {
                onComplete?.Invoke(results, pickHint ?? "无法匹配课表节次");
                yield break;
            }

            bool anyHttpSuccess = false;
            int periodForAdvance = 0;

            foreach (var entry in toAttend)
            {
                int period = Mathf.Clamp(entry.slot.period, 1, 4);
                int dow = Mathf.Clamp(entry.slot.day_of_week, 0, 4);

                bool done = false;
                AttendClassResponseV21 result = null;
                string err = null;
                APIManager.Instance.AttendClassV21(entry.courseId, entry.attendanceStatus, dow, period,
                    r => { result = r; done = true; },
                    e => { err = e; done = true; });
                while (!done) yield return null;

                if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogWarning($"[ServerAttendClassFlow] {entry.courseId}: {err}");
                    results.Add(null);
                    continue;
                }

                if (result != null)
                {
                    results.Add(result);
                    if (result.success)
                        ClassPeriodAttendanceSession.RegisterManualAttend(time, dow, period, entry.courseId);
                    PlayerManager.Instance?.ProcessAttendClassResultV21(result);
                    if (result.success)
                    {
                        anyHttpSuccess = true;
                        periodForAdvance = period;
                    }
                }
                else
                    results.Add(null);
            }

            if (anyHttpSuccess && advanceTimeAfterSuccess && APIManager.Instance != null)
            {
                TimeInfoV21 tAfter = null;
                bool t2d = false;
                APIManager.Instance.GetTimeV21(t => { tAfter = t; t2d = true; }, _ => t2d = true);
                while (!t2d) yield return null;

                if (tAfter != null)
                {
                    int rem = ClassPeriodAttendance.MinutesFromNowToPeriodEnd(tAfter.hour, tAfter.minute, periodForAdvance);
                    if (rem > 0)
                    {
                        bool advDone = false;
                        APIManager.Instance.AdvanceTimeMinutesV21(rem,
                            () => advDone = true,
                            e =>
                            {
                                if (!string.IsNullOrEmpty(e))
                                    Debug.LogWarning("[ServerAttendClassFlow] 推进到下课失败: " + e);
                                advDone = true;
                            });
                        while (!advDone) yield return null;
                    }
                }

                PlayerManager.Instance?.RefreshFromServer();
            }

            onComplete?.Invoke(results, null);
        }
    }
}
