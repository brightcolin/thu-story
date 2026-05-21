using System;
using UnityEngine;
using TMPro;
using QinghuaStory;

/// <summary>
/// 游戏时间 HUD：将日期、时间分别显示到 Date、Time 两个 TMP_Text 中。
/// 日期用 GET /time 的 date_display；时刻由 hour/minute 格式化为 12 小时制并向下取整到 10 分钟（如 6:30am、6:40am）。
/// </summary>
public class GameTimeHUD : MonoBehaviour
{
    private const string NoApiDatePlaceholder = "日期：连接中…";
    private const string NoApiTimePlaceholder = "时间：连接中…";

    private static readonly string[] WeekdayNamesCn =
        { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };

    [Header("显示文本")]
    [Tooltip("显示日期，例如：大一上学期第一周")]
    public TMP_Text Date;

    [Tooltip("时刻行：星期 + 时刻，分钟向下对齐到 10 分")]
    public TMP_Text Time;

    [Header("v2.1 后端时间刷新")]
    [Tooltip("每隔多少秒从后端拉取一次 /time")]
    public float refreshInterval = 3f;

    private float _timer;
    private string _lastShownTimeToken;

    private void Start()
    {
        APIManager.EnsureExists();
        if (APIManager.Instance != null)
            RequestTimeFromServer();
    }

    private void Update()
    {
        if (!TryRefreshV21Time())
            ApplyNoApiPlaceholder();
    }

    /// <summary>有 APIManager 时轮询 /time；无则返回 false。</summary>
    private bool TryRefreshV21Time()
    {
        APIManager.EnsureExists();
        if (APIManager.Instance == null) return false;

        if (DailySummaryUI.SuppressServerTimePoll)
            return true;

        if (refreshInterval <= 0.01f) refreshInterval = 0.5f;
        _timer += UnityEngine.Time.unscaledDeltaTime;
        if (_timer < refreshInterval) return true;
        _timer = 0f;

        RequestTimeFromServer();
        return true;
    }

    private void ApplyNoApiPlaceholder()
    {
        if (Date != null && Date.text != NoApiDatePlaceholder)
            Date.text = NoApiDatePlaceholder;
        if (Time != null && Time.text != NoApiTimePlaceholder)
            Time.text = NoApiTimePlaceholder;
    }

    private void RequestTimeFromServer()
    {
        APIManager.Instance.GetTimeV21(ApplyBackendTimeToUi, _ => { });
    }

    private void ApplyBackendTimeToUi(TimeInfoV21 t)
    {
        if (t == null) return;

        PlayerManager.Instance?.UpdateClientTimeCacheFromTimeApi(t);

        if (Date != null && !string.IsNullOrEmpty(t.date_display) && Date.text != t.date_display)
            Date.text = t.date_display;

        if (Time == null) return;

        int h = t.hour;
        int m = t.minute;
        if (IsLateNightPhase(t) && h == 12)
            h = 0;

        string weekday = ResolveWeekdayName(t);
        string clock = FormatTime12hTenMinuteStep(h, m);
        string timeStr = string.IsNullOrEmpty(weekday) ? clock : $"{weekday} {clock}";

        int mStep = (m / 10) * 10;
        string token = string.IsNullOrEmpty(weekday)
            ? $"{h}:{mStep:D2}"
            : $"{weekday}|{h}:{mStep:D2}";

        if (_lastShownTimeToken != token)
        {
            _lastShownTimeToken = token;
            Time.text = timeStr;
        }
    }

    private static string ResolveWeekdayName(TimeInfoV21 t)
    {
        if (t == null) return "";
        if (!string.IsNullOrEmpty(t.weekday_name))
            return t.weekday_name.Trim();
        if (t.weekday < 0 || t.weekday > 6)
            return "";
        return WeekdayNamesCn[t.weekday];
    }

    private static bool IsLateNightPhase(TimeInfoV21 t)
    {
        if (t == null) return false;
        if (!string.IsNullOrEmpty(t.phase) &&
            t.phase.Equals("Night", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(t.phase_name) && t.phase_name == "深夜")
            return true;
        return false;
    }

    private static string FormatTime12hTenMinuteStep(int hour24, int minute)
    {
        minute = (minute / 10) * 10;
        hour24 = ((hour24 % 24) + 24) % 24;

        int h12;
        string ampm;
        if (hour24 == 0)
        {
            h12 = 0;
            ampm = "am";
        }
        else if (hour24 < 12)
        {
            h12 = hour24;
            ampm = "am";
        }
        else if (hour24 == 12)
        {
            h12 = 12;
            ampm = "pm";
        }
        else
        {
            h12 = hour24 - 12;
            ampm = "pm";
        }

        return $"{h12}:{minute:D2}{ampm}";
    }
}
