using UnityEngine;
using QinghuaStory;

/// <summary>
/// 按 v2.1 文档 Q5，在上课时间段内提示「该上某课了」。挂在任意常驻物体上（可与 BackendGameplayMenu 同场景）。
/// </summary>
public class ClassPeriodNotifier : MonoBehaviour
{
    [Tooltip("轮询间隔（秒，不受 timeScale 影响）")]
    public float intervalSeconds = 30f;

    [Tooltip("同一课程同一节次重复提示的最短间隔（秒）")]
    public float cooldownSeconds = 120f;

    private float _timer;
    private float _lastNotifyRealtime;

    private static readonly int[] PeriodStartHour = { 0, 8, 9, 13, 19 };

    private void Start()
    {
        APIManager.EnsureExists();
    }

    private void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < intervalSeconds) return;
        _timer = 0f;
        if (APIManager.Instance == null) return;
        APIManager.Instance.GetTimeV21(CheckWithTime, _ => { });
    }

    private void CheckWithTime(TimeInfoV21 time)
    {
        if (time == null || time.is_game_over) return;
        APIManager.Instance.GetScheduleV21(sched => EvaluateSchedule(time, sched), _ => { });
    }

    private void EvaluateSchedule(TimeInfoV21 time, ScheduleResponseV21 sched)
    {
        if (sched?.schedule == null) return;
        int h = time.hour;
        int wd = time.weekday;
        foreach (var slot in sched.schedule)
        {
            if (slot == null || string.IsNullOrEmpty(slot.course_id)) continue;
            if (slot.day_of_week != wd) continue;
            int p = slot.period;
            if (p < 1 || p > 4) continue;
            int start = PeriodStartHour[p];
            if (h < start || h >= start + 2) continue;

            string key = $"{wd}_{p}_{slot.course_id}";
            if (Time.realtimeSinceStartup - _lastNotifyRealtime < cooldownSeconds) return;
            _lastNotifyRealtime = Time.realtimeSinceStartup;
            GameHUD.Instance?.ShowNotification($"该上「{slot.course_name}」了！", 6f);
            return;
        }
    }
}
