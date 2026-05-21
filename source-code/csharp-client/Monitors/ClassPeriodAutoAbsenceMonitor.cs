using System;
using System.Collections;
using System.Collections.Generic;
using QinghuaStory;
using UnityEngine;

/// <summary>
/// 当游戏时间已达/已过课表节次下课点且玩家未成功签到时，自动 POST /class/attend（absent）并刷新属性。
/// 活动推进时间后会立即尝试扫描（不仅依赖定时轮询）。
/// </summary>
public class ClassPeriodAutoAbsenceMonitor : MonoBehaviour
{
    public static ClassPeriodAutoAbsenceMonitor Instance { get; private set; }

    [Tooltip("轮询间隔（秒，不受 timeScale 影响）")]
    public float intervalSeconds = 25f;

    float _timer;
    bool _scanQueued;
    Coroutine _scanCoroutine;

    void OnEnable()
    {
        Instance = this;
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        APIManager.EnsureExists();
    }

    /// <summary>时间因活动、POST /time/advance 等变化后调用，尽快补记缺勤。</summary>
    public static void RequestScanAfterTimeChange()
    {
        if (Instance == null || APIManager.Instance == null)
            return;
        Instance.ScheduleScan();
    }

    void ScheduleScan()
    {
        _scanQueued = true;
        if (_scanCoroutine == null)
            _scanCoroutine = StartCoroutine(ScanLoop());
    }

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < intervalSeconds)
            return;
        _timer = 0f;
        if (APIManager.Instance == null)
            return;
        ScheduleScan();
    }

    IEnumerator ScanLoop()
    {
        do
        {
            _scanQueued = false;

            TimeInfoV21 time = null;
            bool timeDone = false;
            APIManager.Instance.GetTimeV21(
                t => { time = t; timeDone = true; },
                _ => { timeDone = true; });
            while (!timeDone)
                yield return null;

            if (time != null && !time.is_game_over && time.weekday >= 0 && time.weekday <= 4)
            {
                ScheduleResponseV21 sched = null;
                bool schedDone = false;
                APIManager.Instance.GetScheduleV21(
                    s => { sched = s; schedDone = true; },
                    _ => { schedDone = true; });
                while (!schedDone)
                    yield return null;

                yield return RunAutoAbsentRoutine(time, sched);
            }
        } while (_scanQueued);

        _scanCoroutine = null;
    }

    IEnumerator RunAutoAbsentRoutine(TimeInfoV21 time, ScheduleResponseV21 sched)
    {
        if (sched?.schedule == null || sched.schedule.Length == 0)
            yield break;

        int wd = time.weekday;
        var pending = new List<(string courseId, int dow, int period)>();
        var dedupePeriodCourse = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in sched.schedule)
        {
            if (s == null || string.IsNullOrEmpty(s.course_id))
                continue;
            if (s.day_of_week != wd)
                continue;
            int p = Mathf.Clamp(s.period, 1, 4);
            if (!ClassPeriodAttendance.IsPeriodEnded(time.hour, time.minute, p))
                continue;
            string key = ClassPeriodAttendanceSession.MakeKey(time, wd, p, s.course_id);
            if (ClassPeriodAttendanceSession.WasHandled(key))
                continue;
            string dkey = p + "|" + s.course_id;
            if (!dedupePeriodCourse.Add(dkey))
                continue;
            pending.Add((s.course_id, Mathf.Clamp(s.day_of_week, 0, 4), p));
        }

        if (pending.Count == 0)
            yield break;

        foreach (var item in pending)
        {
            bool done = false;
            AttendClassResponseV21 res = null;
            string err = null;
            APIManager.Instance.AttendClassV21(item.courseId, "absent", item.dow, item.period,
                r => { res = r; done = true; },
                e => { err = e; done = true; });
            while (!done)
                yield return null;

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning($"[ClassPeriodAutoAbsenceMonitor] 自动缺勤失败 {item.courseId}: {err}");
                continue;
            }

            if (res != null && res.success)
                ClassPeriodAttendanceSession.RegisterAutoAbsentSent(time, wd, item.period, item.courseId);
        }

        PlayerManager.Instance?.RefreshFromServer();
    }
}
