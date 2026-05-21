using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 选课环节：拉取可选课列表，逐门提交 POST /courses/select（课时安排由学分自动排布，与 BackendGameplayMenu 一致）。
/// 触发：新学期 semester_index 变化、或开局检测课表为空。与《前端对接指南_v2.1》§8、Q6 一致。
/// </summary>
public class CourseSelectionUI : MonoBehaviour
{
    [Header("何时打开")]
    public bool openOnSemesterChanged = true;
    [Tooltip("解决大一上等首次开局：尚无学期切换事件时，若课表为空仍弹出选课。")]
    public bool openWhenScheduleEmptyAfterStart = true;
    [Tooltip("等待 PlayerManager / 服务端首轮同步后再查课表（秒）。")]
    public float initialScheduleCheckDelay = 0.75f;

    [Header("UI 引用（可全部留空：首次打开时会自动生成简易界面）")]
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text hintText;
    [Tooltip("显示当前已选课程列表与总学分；留空则运行时自动创建")]
    public TMP_Text enrolledSummaryText;
    [Tooltip("列表容器，建议带 Vertical Layout Group + Content Size Fitter（子项高度）")]
    public RectTransform courseListParent;
    public Button closeButton;

    [Header("打开时行为")]
    public bool pauseServerTimeWhileOpen = true;
    public bool disablePlayerMoveWhileOpen = true;

    [Header("字体（与人物属性 UI 一致：思源黑体）")]
    [Tooltip("留空则自动使用本面板已绑定的 TMP 字体，或从场景中 text 脚本的 t1～t9 继承（与属性面板同源）")]
    public TMP_FontAsset chineseFontAsset;

    private bool _runtimeBuilt;
    private TMP_FontAsset _cachedResolvedFont;
    private readonly List<GameObject> _dynamicRows = new();
    private playercontrol _pc;
    private bool _open;
    private bool _pauseHeld;

    private void Awake()
    {
        APIManager.EnsureExists();
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);
    }

    private void OnEnable()
    {
        PlayerManager.SemesterIndexChanged += OnSemesterIndexChanged;
    }

    private void OnDisable()
    {
        PlayerManager.SemesterIndexChanged -= OnSemesterIndexChanged;
        if (_open)
            ClosePanel();
    }

    private IEnumerator Start()
    {
        if (openWhenScheduleEmptyAfterStart)
            yield return CheckScheduleAndOpenIfEmptyRoutine(initialScheduleCheckDelay,
                "本学期尚未选课，请从下列课程中选择（可多次选修不同课程）");
    }

    /// <summary>
    /// 服务端 POST /save/reset 并重拉 /player 之后调用。Restart 不重载场景时 Start 不会再次执行，
    /// 且学期序号仍为 0 时不会触发 SemesterIndexChanged，必须在此再次查课表。
    /// </summary>
    public void AfterFullGameRestart()
    {
        var co = CheckScheduleAndOpenIfEmptyRoutine(0.05f,
            "回档完成，请重新选修本学期课程（可多次点击不同课程）");
        if (isActiveAndEnabled)
            StartCoroutine(co);
        else if (PlayerManager.Instance != null)
            PlayerManager.Instance.StartCoroutine(co);
        else
            Debug.LogWarning("[CourseSelectionUI] 回档后无法检测课表：物体未激活且 PlayerManager 不存在。");
    }

    private IEnumerator CheckScheduleAndOpenIfEmptyRoutine(float delaySeconds, string headlineWhenEmpty)
    {
        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);
        if (APIManager.Instance == null)
            yield break;

        bool doneS = false;
        ScheduleResponseV21 sched = null;
        string err = null;

        APIManager.Instance.GetScheduleV21(
            data => { sched = data; doneS = true; },
            e => { err = e; doneS = true; });

        while (!doneS) yield return null;
        if (!string.IsNullOrEmpty(err) || sched == null)
            yield break;

        bool empty = IsScheduleEmpty(sched);
        if (empty && !_open)
            OpenPanel(headlineWhenEmpty);
    }

    /// <summary>供按钮或其它系统手动打开。</summary>
    public void OpenManually(string title = "选课")
    {
        OpenPanel(title);
    }

    private void OnSemesterIndexChanged(int prevSemester, int newSemester)
    {
        if (!openOnSemesterChanged)
            return;
        OpenPanel($"进入新学期，请选修本学期课程（学期序号 {newSemester}）");
    }

    private void OpenPanel(string headline)
    {
        EnsureUiExists();
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
            if (titleText != null)
                titleText.text = headline;
            if (hintText != null)
                hintText.text = "点击课程名即可按学分自动排课并提交选课。";
        }
        _open = true;
        RefreshEnrolledSummary();

        if (pauseServerTimeWhileOpen)
        {
            ServerPauseCoordinator.Acquire(this);
            _pauseHeld = true;
        }
        if (disablePlayerMoveWhileOpen)
        {
            if (_pc == null) _pc = FindObjectOfType<playercontrol>();
            if (_pc != null) _pc.canmove = false;
        }

        RefreshCourseList();
    }

    /// <summary>从 GET /courses/mine 拉取已选课程与学分合计，写入 enrolledSummaryText。</summary>
    private void RefreshEnrolledSummary()
    {
        if (!_open || APIManager.Instance == null)
            return;

        if (enrolledSummaryText == null)
        {
            EnsureEnrolledSummaryPlaceholder();
            if (enrolledSummaryText == null)
                return;
        }

        APIManager.Instance.GetMyCoursesV21(
            data =>
            {
                if (!_open || enrolledSummaryText == null) return;
                enrolledSummaryText.text = FormatEnrolledCoursesSummary(data);
            },
            err =>
            {
                if (!_open || enrolledSummaryText == null) return;
                enrolledSummaryText.text = "已选课程：加载失败　" + err;
            });
    }

    private void EnsureEnrolledSummaryPlaceholder()
    {
        if (enrolledSummaryText != null || panelRoot == null)
            return;
        enrolledSummaryText = TmpLabel(panelRoot.transform, "", 12,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -86f), new Vector2(620, 88f));
        enrolledSummaryText.alignment = TextAlignmentOptions.TopLeft;
        enrolledSummaryText.color = new Color(0.92f, 0.94f, 1f, 1f);
    }

    private static string FormatEnrolledCoursesSummary(MyCoursesResponseV21 data)
    {
        if (data?.courses == null || data.courses.Length == 0)
            return "已选课程：无\n总学分：0";

        int total = 0;
        var body = new StringBuilder();
        int count = 0;
        foreach (var c in data.courses)
        {
            if (c == null) continue;
            count++;
            int cr = Mathf.Max(0, c.credits);
            total += cr;
            body.AppendLine($"· {c.course_name}　{cr} 学分");
        }

        return $"已选 {count} 门，总学分 {total}\n{body}";
    }

    public void ClosePanel()
    {
        if (!_open) return;
        _open = false;
        if (panelRoot != null)
            panelRoot.SetActive(false);

        if (_pauseHeld)
        {
            ServerPauseCoordinator.Release(this);
            _pauseHeld = false;
        }
        if (disablePlayerMoveWhileOpen && _pc != null)
            _pc.canmove = true;

        ClearDynamicRows();
    }

    private void EnsureUiExists()
    {
        if (panelRoot != null)
            return;
        if (_runtimeBuilt)
            return;

        BuildRuntimeUi();
        _runtimeBuilt = true;
    }

    private void BuildRuntimeUi()
    {
        var canvasGo = new GameObject("CourseSelectionCanvas");
        DontDestroyOnLoad(canvasGo);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 280;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 640);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        panelRoot = new GameObject("CoursePanel");
        panelRoot.transform.SetParent(canvasGo.transform, false);
        var prt = panelRoot.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.12f, 0.12f);
        prt.anchorMax = new Vector2(0.88f, 0.88f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        panelRoot.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.14f, 0.98f);

        titleText = TmpLabel(panelRoot.transform, "选课", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -14f), new Vector2(520, 40));
        hintText = TmpLabel(panelRoot.transform, "", 13, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -50f), new Vector2(600, 32));
        hintText.enableWordWrapping = true;

        enrolledSummaryText = TmpLabel(panelRoot.transform, "已选课程：加载中…", 12,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -86f), new Vector2(620, 88f));
        enrolledSummaryText.alignment = TextAlignmentOptions.TopLeft;
        enrolledSummaryText.color = new Color(0.92f, 0.94f, 1f, 1f);

        var closeGo = new GameObject("CloseBtn");
        closeGo.transform.SetParent(panelRoot.transform, false);
        var crt = closeGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(1f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-10f, -10f);
        crt.sizeDelta = new Vector2(100, 32);
        closeGo.AddComponent<Image>().color = new Color(0.35f, 0.25f, 0.25f, 1f);
        closeButton = closeGo.AddComponent<Button>();
        closeButton.onClick.AddListener(ClosePanel);
        var closeLbl = TmpLabel(closeGo.transform, "关闭", 14, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        closeLbl.alignment = TextAlignmentOptions.Center;

        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(panelRoot.transform, false);
        var srt = scrollGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(16f, 16f);
        srt.offsetMax = new Vector2(-16f, -188f);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = vrt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.2f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        courseListParent = content.AddComponent<RectTransform>();
        courseListParent.anchorMin = new Vector2(0f, 1f);
        courseListParent.anchorMax = new Vector2(1f, 1f);
        courseListParent.pivot = new Vector2(0.5f, 1f);
        courseListParent.anchoredPosition = Vector2.zero;
        courseListParent.sizeDelta = new Vector2(0, 400f);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vrt;
        scroll.content = courseListParent;
        scroll.vertical = true;
        scroll.horizontal = false;

        panelRoot.SetActive(false);
    }

    private TextMeshProUGUI TmpLabel(Transform parent, string text, int size,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(anchorMin.x, anchorMax.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        ApplyChineseFontTo(tmp);
        return tmp;
    }

    /// <summary>与 SampleScene 人物属性一致：SourceHanSansSC-Regular SDF（guid 与 t1～t9 相同）。</summary>
    private TMP_FontAsset ResolveChineseFont()
    {
        if (chineseFontAsset != null)
            return chineseFontAsset;
        if (_cachedResolvedFont != null)
            return _cachedResolvedFont;

        if (titleText != null && titleText.font != null)
            _cachedResolvedFont = titleText.font;
        else if (hintText != null && hintText.font != null)
            _cachedResolvedFont = hintText.font;
        else if (enrolledSummaryText != null && enrolledSummaryText.font != null)
            _cachedResolvedFont = enrolledSummaryText.font;
        else
        {
            var stats = FindObjectOfType<text>(true);
            if (stats != null)
            {
                TMP_Text[] refs = { stats.t1, stats.t2, stats.t3, stats.t4, stats.t5, stats.t6, stats.t7, stats.t8, stats.t9 };
                foreach (var t in refs)
                {
                    if (t != null && t.font != null)
                    {
                        _cachedResolvedFont = t.font;
                        break;
                    }
                }
            }
        }

        if (_cachedResolvedFont == null)
            _cachedResolvedFont = ThustoryUIFont.GetDefaultCjkFont();
        return _cachedResolvedFont;
    }

    private void ApplyChineseFontTo(TMP_Text tmp)
    {
        if (tmp == null) return;
        var font = ResolveChineseFont();
        if (font == null) return;
        tmp.font = font;
        if (font.material != null)
            tmp.fontSharedMaterial = font.material;
    }

    private void RefreshCourseList()
    {
        ClearDynamicRows();
        if (APIManager.Instance == null || courseListParent == null)
            return;

        APIManager.Instance.GetAvailableCoursesV21(data =>
        {
            if (data?.courses == null || data.courses.Length == 0)
            {
                if (hintText != null)
                    hintText.text = "当前学期无可选课程（或已全部选完）。";
                RefreshEnrolledSummary();
                return;
            }

            if (titleText != null && !string.IsNullOrEmpty(data.semester_name))
                titleText.text = $"{data.semester_name} · 选课";

            foreach (var c in data.courses)
            {
                if (c == null) continue;
                string cid = c.course_id;
                int credits = c.credits;
                string line = $"{c.course_name}　{c.credits} 学分 · {c.course_id}";
                var row = MakeCourseRowButton(courseListParent, line, () => StartCoroutine(SelectOneCourse(cid, credits)));
                var le = row.GetComponent<LayoutElement>();
                if (le == null) le = row.AddComponent<LayoutElement>();
                le.minHeight = 36f;
                le.preferredHeight = 36f;
                _dynamicRows.Add(row);
            }
        }, err =>
        {
            if (hintText != null)
                hintText.text = "拉取可选课程失败：" + err;
            RefreshEnrolledSummary();
        });
    }

    private GameObject MakeCourseRowButton(Transform parent, string label, Action onClick)
    {
        var go = new GameObject("Row_" + label.GetHashCode());
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0, 36);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.32f, 0.48f, 1f);
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        var tgo = new GameObject("Text");
        tgo.transform.SetParent(go.transform, false);
        var trt = tgo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8, 4);
        trt.offsetMax = new Vector2(-8, -4);
        var tmp = tgo.AddComponent<TextMeshProUGUI>();
        tmp.text = "选修 · " + label;
        tmp.fontSize = 14;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        ApplyChineseFontTo(tmp);
        return go;
    }

    private IEnumerator SelectOneCourse(string courseId, int credits)
    {
        ScheduleResponseV21 sched = null;
        string schedErr = null;
        bool schedDone = false;

        APIManager.Instance.GetScheduleV21(s => { sched = s; schedDone = true; }, e => { schedErr = e; schedDone = true; });
        while (!schedDone) yield return null;
        if (!string.IsNullOrEmpty(schedErr))
        {
            if (hintText != null) hintText.text = "拉取课表失败，无法排课：" + schedErr;
            yield break;
        }

        ScheduleSlotV21[] occupied = sched?.schedule;

        var slots = CourseScheduleUtil.BuildAutoSlots(credits, courseId, occupied);
        SelectCourseResponseV21 res = null;
        string err = null;
        bool done = false;
        APIManager.Instance.SelectCourseV21(courseId, slots,
            r => { res = r; done = true; },
            e => { err = e; done = true; });
        while (!done) yield return null;

        if (hintText == null) { }
        else if (!string.IsNullOrEmpty(err))
            hintText.text = "选课失败：" + err;
        else if (res != null && res.success)
            hintText.text = $"已选：{courseId}，可继续选其它课或点关闭。";
        else
            hintText.text = "服务端未接受本次选课。";

        PlayerManager.Instance?.RefreshFromServer();
        RefreshEnrolledSummary();
        RefreshCourseList();
    }

    private void ClearDynamicRows()
    {
        foreach (var go in _dynamicRows)
            if (go != null) Destroy(go);
        _dynamicRows.Clear();
    }

    private static bool IsScheduleEmpty(ScheduleResponseV21 sched)
    {
        if (sched.schedule == null || sched.schedule.Length == 0)
            return true;
        foreach (var s in sched.schedule)
            if (s != null && !string.IsNullOrEmpty(s.course_id))
                return false;
        return true;
    }
}
