using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 每日总结：第1页含属性、好感、课程掌握度；第2页在「日末」显示明日课表、在「睡醒」显示本日课表；鼠标左键翻页；
/// 关闭时 POST /time/nextday 或已推进则不再跳日；打开期间服务端暂停。
/// </summary>
public class DailySummaryUI : MonoBehaviour
{
    public static DailySummaryUI Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    /// <summary>为 true 时 <see cref="GameTimeHUD"/> 停止轮询 GET /time。</summary>
    public static bool SuppressServerTimePoll { get; private set; }

    private static readonly string[] PeriodTimeHints =
    {
        "",
        "第1节 8:00–9:30",
        "第2节 9:50–12:10",
        "第3节 13:30–15:00",
        "第4节 19:20–21:00"
    };

    private static readonly string[] WeekdayNamesCn =
        { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    /// <summary>含 Canvas 的子物体；仅隐藏此子物体，根节点保持 Active 以便协程/Update 可运行。</summary>
    private GameObject _canvasHost;
    private Canvas _canvas;
    private CanvasGroup _rootGroup;
    private TMP_Text _titleText;
    private TMP_Text _bodyText;
    private TMP_Text _hintText;

    private playercontrol _player;
    private int _pageIndex;
    private bool _pauseHeld;
    private bool _moveDisabled;
    private bool _closing;
    private bool _opening;
    /// <summary>为 true 时关闭 UI 不再 POST /time/nextday（例如已在宿舍睡觉推进到次日 6:30）。</summary>
    private bool _skipAdvanceOnClose;
    private string _page1Content = "";
    private string _page2Content = "";
    private string _page2Title = "明日课表";
    private TimeInfoV21 _timeFrozen;
    private float _lastPageFlipUnscaledTime = -999f;
    private const float PageFlipDebounceSeconds = 0.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        IsOpen = false;
        SuppressServerTimePoll = false;
    }

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("DailySummaryUI");
        DontDestroyOnLoad(go);
        go.AddComponent<DailySummaryUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUi();
        if (_canvasHost != null) _canvasHost.SetActive(false);
        IsOpen = false;
        SuppressServerTimePoll = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void BuildUi()
    {
        _canvasHost = new GameObject("SummaryCanvas", typeof(RectTransform));
        _canvasHost.transform.SetParent(transform, false);
        var hostRt = _canvasHost.GetComponent<RectTransform>();
        hostRt.anchorMin = Vector2.zero;
        hostRt.anchorMax = Vector2.one;
        hostRt.offsetMin = hostRt.offsetMax = Vector2.zero;

        _canvas = _canvasHost.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9200;

        var scaler = _canvasHost.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _canvasHost.AddComponent<GraphicRaycaster>();

        _rootGroup = _canvasHost.AddComponent<CanvasGroup>();
        _rootGroup.blocksRaycasts = true;
        _rootGroup.interactable = true;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(_canvasHost.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.08f, 0.1f, 0.14f, 0.97f);
        panelImg.raycastTarget = true;
        var sink = panel.AddComponent<DailySummaryPageInputSink>();
        sink.Initialize(this);

        _titleText = CreateText("Title", panel.transform,
            new Vector2(0.5f, 0.88f), new Vector2(0.92f, 0.1f),
            40, FontStyles.Bold, TextAlignmentOptions.Center);
        _titleText.color = new Color(0.95f, 0.95f, 1f);

        _bodyText = CreateText("Body", panel.transform,
            new Vector2(0.5f, 0.52f), new Vector2(0.88f, 0.62f),
            28, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        _bodyText.color = new Color(0.88f, 0.9f, 0.95f);

        _hintText = CreateText("Hint", panel.transform,
            new Vector2(0.5f, 0.06f), new Vector2(0.9f, 0.08f),
            22, FontStyles.Italic, TextAlignmentOptions.Center);
        _hintText.color = new Color(0.65f, 0.7f, 0.78f);
    }

    private static TMP_Text CreateText(string name, Transform parent, Vector2 pivot, Vector2 sizeNorm,
        float fontSize, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(pivot.x - sizeNorm.x * 0.5f, pivot.y - sizeNorm.y * 0.5f);
        rt.anchorMax = new Vector2(pivot.x + sizeNorm.x * 0.5f, pivot.y + sizeNorm.y * 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.text = "";
        return tmp;
    }

    /// <summary>无场景 EventSystem 时 UI 指针事件无效；打开总结前保证存在一个。</summary>
    private static void EnsureEventSystemExists()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    /// <summary>由 <see cref="DayEndSummaryMonitor"/> 或调试调用：拉取时间与课表并显示。</summary>
    public void OpenFromDayEnd()
    {
        if (IsOpen || _closing || _opening) return;
        StartCoroutine(OpenRoutine(skipAdvanceOnClose: false));
    }

    /// <summary>
    /// 宿舍通宵睡觉后：时间已在次日早晨，仅补看总结；关闭时不再次推进时间。
    /// </summary>
    public void OpenAfterOvernightSleep()
    {
        if (IsOpen || _closing || _opening) return;
        StartCoroutine(OpenRoutine(skipAdvanceOnClose: true));
    }

    private IEnumerator OpenRoutine(bool skipAdvanceOnClose)
    {
        _opening = true;
        _skipAdvanceOnClose = skipAdvanceOnClose;
        EnsureEventSystemExists();
        APIManager.EnsureExists();
        if (APIManager.Instance == null)
        {
            _opening = false;
            yield break;
        }

        TimeInfoV21 timeCopy = null;
        bool tDone = false;
        APIManager.Instance.GetTimeV21(t => { timeCopy = t; tDone = true; }, _ => tDone = true);
        while (!tDone) yield return null;
        if (timeCopy == null)
        {
            _opening = false;
            yield break;
        }

        _timeFrozen = timeCopy;
        ScheduleResponseV21 sched = null;
        bool sDone = false;
        APIManager.Instance.GetScheduleV21(
            s => { sched = s; sDone = true; },
            _ => { sched = new ScheduleResponseV21(); sDone = true; });
        while (!sDone) yield return null;

        MyCoursesResponseV21 mineNow = null;
        bool mDone = false;
        APIManager.Instance.GetMyCoursesV21(
            m => { mineNow = m; mDone = true; },
            _ => mDone = true);
        while (!mDone) yield return null;

        _page1Content = BuildPage1Text(mineNow);
        _page2Content = BuildPage2Text(timeCopy, sched ?? new ScheduleResponseV21(), _skipAdvanceOnClose, out _page2Title);

        CachePlayerRef();
        ServerPauseCoordinator.Acquire(this);
        _pauseHeld = true;
        SuppressServerTimePoll = true;

        if (_player == null) _player = FindObjectOfType<playercontrol>();
        if (_player != null)
        {
            _player.canmove = false;
            _moveDisabled = true;
        }

        _pageIndex = 0;
        ApplyPageVisuals();
        if (_canvasHost != null) _canvasHost.SetActive(true);
        IsOpen = true;
        _closing = false;
        _opening = false;
    }

    private void CachePlayerRef()
    {
        if (_player == null) _player = FindObjectOfType<playercontrol>();
    }

    private string BuildPage1Text(MyCoursesResponseV21 mineNow)
    {
        var pm = PlayerManager.Instance;
        if (pm == null) return "无法读取玩家状态。";
        return DailyProgressBaseline.BuildDeltaSummary(pm, mineNow);
    }

    /// <param name="isMorningAfterSleep">true：宿舍睡到新一天早晨，第二页展示 <paramref name="t.weekday"/> 当日课表；false：日末/宵禁，展示日历「明日」<c>(weekday+1)%7</c>。</param>
    private static string BuildPage2Text(TimeInfoV21 t, ScheduleResponseV21 sched, bool isMorningAfterSleep, out string pageTitle)
    {
        int wd = t.weekday;
        if (wd < 0 || wd > 6) wd = 0;

        var sb = new StringBuilder();
        List<SchedLine> hintLines;

        if (isMorningAfterSleep)
        {
            pageTitle = "本日课表";
            if (wd <= 4)
            {
                string heading = $"本日：{WeekdayNamesCn[wd]}";
                hintLines = CollectScheduleLinesForDay(sched, wd);
                AppendScheduleSection(sb, heading, hintLines);
            }
            else
            {
                string heading = $"本日：{WeekdayNamesCn[wd]}（周末）";
                hintLines = new List<SchedLine>();
                AppendWeekendSection(sb, sched, wd, heading, out var previewHint);
                if (previewHint.Count > 0) hintLines = previewHint;
            }
        }
        else
        {
            pageTitle = "明日课表";
            int tomorrow = (wd + 1) % 7;
            if (tomorrow <= 4)
            {
                string heading = $"明日：{WeekdayNamesCn[tomorrow]}";
                hintLines = CollectScheduleLinesForDay(sched, tomorrow);
                AppendScheduleSection(sb, heading, hintLines);
            }
            else
            {
                string heading = $"明日：{WeekdayNamesCn[tomorrow]}（周末）";
                hintLines = new List<SchedLine>();
                AppendWeekendSection(sb, sched, wd, heading, out var previewHint);
                if (previewHint.Count > 0) hintLines = previewHint;
            }
        }

        sb.AppendLine();
        sb.AppendLine("— 提示 —");
        foreach (var hint in BuildActivityHints(hintLines))
            sb.AppendLine("· " + hint);
        sb.AppendLine("· 【贴士】" + DailySummaryFlavorTips.PickRandom());
        return sb.ToString().TrimEnd();
    }

    private static void AppendScheduleSection(StringBuilder sb, string headingLine, List<SchedLine> lines)
    {
        sb.AppendLine(headingLine);
        sb.AppendLine();
        if (lines.Count == 0)
            sb.AppendLine("该日无课，可适当安排自习与活动。");
        else
        {
            lines.Sort((a, b) => a.period.CompareTo(b.period));
            foreach (var line in lines)
                sb.AppendLine($"{PeriodTimeHints[line.period]} · {line.courseName}");
        }
    }

    /// <summary>周末无 0–4 列时说明，并可选列出「接下来首个 Mon–Fri」课表预览；<paramref name="previewForHints"/> 供提示逻辑参考有课形态。</summary>
    private static void AppendWeekendSection(StringBuilder sb, ScheduleResponseV21 sched, int currentWeekday, string headingLine,
        out List<SchedLine> previewForHints)
    {
        previewForHints = new List<SchedLine>();
        sb.AppendLine(headingLine);
        sb.AppendLine();
        sb.AppendLine("该日无周一至周五课表排课。");
        int nextSchool = NextSchoolDayDow(currentWeekday);
        if (nextSchool < 0) return;
        previewForHints = CollectScheduleLinesForDay(sched, nextSchool);
        if (previewForHints.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"接下来首个上课日（{WeekdayNamesCn[nextSchool]}）预览：");
        previewForHints.Sort((a, b) => a.period.CompareTo(b.period));
        foreach (var line in previewForHints)
            sb.AppendLine($"{PeriodTimeHints[line.period]} · {line.courseName}");
    }

    private struct SchedLine
    {
        public int period;
        public string courseName;
    }

    private static List<SchedLine> CollectScheduleLinesForDay(ScheduleResponseV21 sched, int day0Mon)
    {
        var list = new List<SchedLine>();
        if (sched?.schedule == null) return list;
        foreach (var s in sched.schedule)
        {
            if (s == null || string.IsNullOrEmpty(s.course_id)) continue;
            int d = s.day_of_week;
            if (d < 0) d = 0;
            if (d > 4) continue;
            if (d != day0Mon) continue;
            int p = Mathf.Clamp(s.period, 1, 4);
            string name = string.IsNullOrEmpty(s.course_name) ? s.course_id : s.course_name;
            list.Add(new SchedLine { period = p, courseName = name });
        }

        return list;
    }

    /// <summary>从当前 weekday 起，「次日及以后」第一个落在周一至周五的 day_of_week（仅用于周末后预览下一上课日）。</summary>
    public static int NextSchoolDayDow(int currentWeekday0Mon)
    {
        for (int step = 1; step <= 7; step++)
        {
            int c = (currentWeekday0Mon + step) % 7;
            if (c >= 0 && c <= 4) return c;
        }
        return -1;
    }

    private static List<string> BuildActivityHints(List<SchedLine> scheduleLines)
    {
        var hints = new List<string>();
        bool h1 = false, h2 = false, h3 = false, h4 = false;
        foreach (var l in scheduleLines)
        {
            if (l.period == 1) h1 = true;
            else if (l.period == 2) h2 = true;
            else if (l.period == 3) h3 = true;
            else if (l.period == 4) h4 = true;
        }

        int n = scheduleLines.Count;
        bool full = h1 && h2 && h3 && h4;
        if (full)
            hints.Add("四节课全满，节奏紧：带好教材与水壶，课间记得补水。");
        else if (h1 && h2)
            hints.Add("第1、2节相连，建议提前到教室；课间可备一点零食提神。");
        else if (h1 && !h2 && h3 && !h4)
            hints.Add("上午有一、三节而第二节空档，正好慢慢吃午餐或在自习区休整。");
        else if (h1 && !h2 && !h3 && !h4)
            hints.Add("仅早上有课，下午时间可安排自习、活动或补觉。");
        else if (!h1 && !h2 && (h3 || h4))
            hints.Add("上午无课，适合图书馆或校园活动；午饭后留足精神给下午/晚间。");
        else if (h4 && !h1 && !h2 && !h3)
            hints.Add("只有晚间一节，白天可自由安排，别透支精力到夜课。");
        else if (h3 && h4)
            hints.Add("下午与晚间有课，午餐别凑合，晚餐前也可预留短休。");
        else
        {
            if (h1) hints.Add("明早有课，预留出门与早餐时间。");
            if (h2 && !h1) hints.Add("上午后半段有课，可在课前用餐或休息。");
            if (h3 || h4) hints.Add("下午或晚间有课，注意午餐/晚餐与精力分配。");
        }

        if (n == 0)
            hints.Add("无课日可安排图书馆自习、社团或校园活动。");
        hints.Add("8:00–18:00 可尝试帮助游客等日间活动。");
        hints.Add("规律三餐，避免缺餐扣健康。");
        return hints;
    }

    private void ApplyPageVisuals()
    {
        if (_pageIndex == 0)
        {
            _titleText.text = "今日总结";
            _bodyText.text = _page1Content;
            _hintText.text = "鼠标左键 · 下一页";
        }
        else
        {
            _titleText.text = string.IsNullOrEmpty(_page2Title) ? "明日课表" : _page2Title;
            _bodyText.text = _page2Content;
            _hintText.text = _skipAdvanceOnClose
                ? "鼠标左键 · 关闭（已开始新的一天）"
                : "鼠标左键 · 结束一天并休息至次日 6:30";
        }
    }

    /// <summary>由 <see cref="DailySummaryPageInputSink"/> 与 <see cref="Update"/> 左键兜底共用。</summary>
    public void TryAdvancePageFromUser()
    {
        if (!IsOpen || _closing) return;
        if (Time.unscaledTime - _lastPageFlipUnscaledTime < PageFlipDebounceSeconds) return;
        _lastPageFlipUnscaledTime = Time.unscaledTime;

        if (_pageIndex == 0)
        {
            _pageIndex = 1;
            ApplyPageVisuals();
        }
        else
            StartCoroutine(CloseAndAdvanceDayRoutine());
    }

    private void Update()
    {
        if (!IsOpen || _closing) return;

        if (Input.GetMouseButtonDown(0))
            TryAdvancePageFromUser();
    }

    private IEnumerator CloseAndAdvanceDayRoutine()
    {
        _closing = true;

        if (!_skipAdvanceOnClose)
        {
            APIManager.EnsureExists();
            bool advanceOk = false;
            string advErr = null;

            if (APIManager.Instance != null)
            {
                bool done = false;
                APIManager.Instance.NextDayV21(
                    _ => { advanceOk = true; done = true; },
                    e => { advErr = e; advanceOk = false; done = true; });
                while (!done) yield return null;
            }

            if (!advanceOk)
            {
                Debug.LogWarning("[DailySummaryUI] POST /time/nextday 失败: " + advErr + "，尝试解除暂停后按分钟推进。");
                if (_pauseHeld)
                {
                    ServerPauseCoordinator.Release(this);
                    _pauseHeld = false;
                }

                int minutes = _timeFrozen != null
                    ? ActivitySceneIdsV21.MinutesToNextGameDayBlockStart(_timeFrozen)
                    : ActivitySceneIdsV21.GameDayMinutes;
                if (minutes <= 0) minutes = ActivitySceneIdsV21.GameDayMinutes;

                bool advDone = false;
                string adv2 = null;
                APIManager.Instance.AdvanceTimeMinutesV21(minutes, () => advDone = true, e => { adv2 = e; advDone = true; });
                while (!advDone) yield return null;
                if (!string.IsNullOrEmpty(adv2))
                    Debug.LogWarning("[DailySummaryUI] /time/advance?minutes= 失败: " + adv2);
            }
        }

        _skipAdvanceOnClose = false;

        if (_pauseHeld)
        {
            ServerPauseCoordinator.Release(this);
            _pauseHeld = false;
        }

        SuppressServerTimePoll = false;

        if (_moveDisabled && _player != null)
        {
            _player.canmove = true;
            _moveDisabled = false;
        }

        if (_canvasHost != null) _canvasHost.SetActive(false);
        IsOpen = false;
        _closing = false;

        var pm = PlayerManager.Instance;
        if (pm != null)
        {
            bool refreshed = false;
            pm.RefreshFromServer(() => refreshed = true);
            while (!refreshed) yield return null;
            MyCoursesResponseV21 mineClosing = null;
            bool mDone = false;
            if (APIManager.Instance != null)
            {
                APIManager.Instance.GetMyCoursesV21(
                    m => { mineClosing = m; mDone = true; },
                    _ => mDone = true);
                while (!mDone) yield return null;
            }
            DailyProgressBaseline.CaptureFrom(pm, mineClosing);
        }

        ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
    }

    /// <summary>读档等需要时：若 UI 误开，强制关闭并恢复暂停计数（不推进时间）。</summary>
    public void ForceCloseWithoutAdvancing()
    {
        StopAllCoroutines();
        _skipAdvanceOnClose = false;
        if (_pauseHeld)
        {
            ServerPauseCoordinator.Release(this);
            _pauseHeld = false;
        }
        SuppressServerTimePoll = false;
        if (_moveDisabled && _player != null)
        {
            _player.canmove = true;
            _moveDisabled = false;
        }
        if (_canvasHost != null) _canvasHost.SetActive(false);
        IsOpen = false;
        _closing = false;
    }
}

/// <summary>
/// 全屏底图仅接收鼠标左键（走 EventSystem，避免 TMP 挡射线或旧 Input 与 UI 模块不同步导致无效）。
/// </summary>
[DisallowMultipleComponent]
internal sealed class DailySummaryPageInputSink : MonoBehaviour, IPointerDownHandler
{
    private DailySummaryUI _host;

    public void Initialize(DailySummaryUI host) => _host = host;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_host == null) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        _host.TryAdvancePageFromUser();
    }
}
