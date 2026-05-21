using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 图书馆场景（scene 11）按 E：展示本学期课表中的课程供选择，执行 POST /activities/execute（v2.1，可选 course_id）。
/// </summary>
public class LibrarySelfStudyUI : MonoBehaviour
{
    public static LibrarySelfStudyUI Instance { get; private set; }

    /// <summary>自习选课面板是否打开（供图书馆 R 键等避让）。</summary>
    public static bool IsOpen => Instance != null && Instance._open;

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("LibrarySelfStudyUI");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LibrarySelfStudyUI>();
    }

    [Header("行为")]
    public bool pauseServerTimeWhileOpen = true;
    public bool disablePlayerMoveWhileOpen = true;

    private GameObject _canvasRoot;
    private GameObject _panel;
    private TMP_Text _title;
    private TMP_Text _status;
    private RectTransform _listParent;
    private readonly List<GameObject> _rowPool = new();

    private playercontrol _pc;
    private bool _open;
    private bool _pauseHeld;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildUi();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>由 playercontrol 在图书馆按 E 调用。</summary>
    public static void OpenForPlayer(playercontrol pc)
    {
        EnsureExists();
        Instance.DoOpen(pc);
    }

    /// <summary>闭馆驱逐等：收起自习面板并恢复暂停/移动，不执行活动。</summary>
    public static void CloseIfOpenForScenePolicy()
    {
        if (Instance == null) return;
        if (!Instance._open) return;
        Instance.CloseWithoutAction();
    }

    void DoOpen(playercontrol pc)
    {
        if (_open) return;

        if (LibraryHoursV21.TryIsLibraryClosedFromPlayerCache(out bool libClosed) && libClosed)
        {
            ActivityPresentationUI.EnsureExists();
            ActivityPresentationUI.Instance.ShowFailure("图书馆", "闭馆中，无法进行自习。",
                ActivityUnlockHints.LibraryClosedContext);
            return;
        }

        _pc = pc;
        _open = true;

        if (_panel != null)
            _panel.SetActive(true);
        if (_title != null)
            _title.text = "图书馆自习";
        if (_status != null)
            _status.text = "正在加载课表…";

        if (pauseServerTimeWhileOpen)
        {
            ServerPauseCoordinator.Acquire(this);
            _pauseHeld = true;
        }

        if (disablePlayerMoveWhileOpen && _pc != null)
            _pc.canmove = false;

        ClearList();
        StartCoroutine(LoadScheduleAndBuildList());
    }

    private void CloseWithoutAction()
    {
        if (!_open) return;
        _open = false;
        if (_panel != null)
            _panel.SetActive(false);

        if (_pauseHeld)
        {
            ServerPauseCoordinator.Release(this);
            _pauseHeld = false;
        }
        if (disablePlayerMoveWhileOpen && _pc != null)
            _pc.canmove = true;

        ClearList();
        _pc = null;
    }

    private void ConfirmAndRun(string courseIdOrNull)
    {
        if (!_open || _pc == null) return;

        var host = _pc;
        CloseWithoutAction();

        host.StartCoroutine(host.RunLibrarySelfStudyFromUi(courseIdOrNull));
    }

    private IEnumerator LoadScheduleAndBuildList()
    {
        if (APIManager.Instance == null)
        {
            APIManager.EnsureExists();
            if (APIManager.Instance == null)
            {
                if (_status != null) _status.text = "APIManager 未初始化";
                yield break;
            }
        }

        ScheduleResponseV21 sched = null;
        MyCoursesResponseV21 mine = null;
        string err = null;
        bool d1 = false, d2 = false;

        APIManager.Instance.GetScheduleV21(
            s => { sched = s; d1 = true; },
            e => { err = e; d1 = true; });
        APIManager.Instance.GetMyCoursesV21(
            m => { mine = m; d2 = true; },
            e => { if (string.IsNullOrEmpty(err)) err = e; d2 = true; });

        while (!d1 || !d2) yield return null;

        if (!string.IsNullOrEmpty(err))
        {
            if (_status != null) _status.text = "加载失败：" + err;
            yield break;
        }

        var mastery = new Dictionary<string, float>(StringComparer.Ordinal);
        if (mine?.courses != null)
        {
            foreach (var c in mine.courses)
            {
                if (c != null && !string.IsNullOrEmpty(c.course_id))
                    mastery[c.course_id] = c.mastery;
            }
        }

        var slots = sched?.schedule;
        var unique = new Dictionary<string, ScheduleSlotV21>(StringComparer.Ordinal);
        if (slots != null)
        {
            foreach (var s in slots)
            {
                if (s != null && !string.IsNullOrEmpty(s.course_id) && !unique.ContainsKey(s.course_id))
                    unique[s.course_id] = s;
            }
        }

        if (_status != null)
        {
            if (unique.Count == 0)
                _status.text = "本学期课表暂无课程（可先选课）。仍可进行不加科目的自习。";
            else
                _status.text = "请选择要巩固的科目，或选择「不加科目」。";
        }

        var ordered = unique.Values
            .OrderBy(s => s.course_name ?? s.course_id)
            .ToList();

        foreach (var s in ordered)
        {
            float m = mastery.TryGetValue(s.course_id, out var mv) ? mv : float.NaN;
            string line = string.IsNullOrEmpty(s.course_name) ? s.course_id : s.course_name;
            line += $"　{s.credits} 学分";
            if (!float.IsNaN(m))
                line += $"　掌握度 {m:F0}";
            string cid = s.course_id;
            AddRowButton(line, () => ConfirmAndRun(cid));
        }

        AddRowButton("— 不加特定科目（仅自习）—", () => ConfirmAndRun(null), new Color(0.2f, 0.35f, 0.45f, 1f));

        Canvas.ForceUpdateCanvases();
        if (_listParent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_listParent);
    }

    const float RowMinHeight = 58f;

    private void AddRowButton(string label, Action onClick, Color? bg = null)
    {
        var go = new GameObject("Row");
        go.transform.SetParent(_listParent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.localScale = Vector3.one;

        // Image/Button 默认不向 VerticalLayoutGroup 上报高度，必须用 LayoutElement 否则所有行高度为 0、文字叠在一起。
        var rowLayout = go.AddComponent<LayoutElement>();
        rowLayout.minHeight = RowMinHeight;
        rowLayout.preferredHeight = RowMinHeight;
        rowLayout.flexibleHeight = 0f;
        rowLayout.flexibleWidth = 1f;

        var img = go.AddComponent<Image>();
        img.color = bg ?? new Color(0.15f, 0.18f, 0.22f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick?.Invoke());

        var txtGo = new GameObject("Label");
        txtGo.transform.SetParent(go.transform, false);
        var tr = txtGo.AddComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(14f, 8f);
        tr.offsetMax = new Vector2(-14f, -8f);
        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15;
        tmp.lineSpacing = 2f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        ThustoryUIFont.Apply(tmp);

        _rowPool.Add(go);
    }

    private void ClearList()
    {
        foreach (var go in _rowPool)
        {
            if (go != null) Destroy(go);
        }
        _rowPool.Clear();
    }

    private void BuildUi()
    {
        _canvasRoot = new GameObject("LibrarySelfStudyCanvas");
        _canvasRoot.transform.SetParent(transform, false);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 275;
        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 640);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRoot.AddComponent<GraphicRaycaster>();

        _panel = new GameObject("Panel");
        _panel.transform.SetParent(_canvasRoot.transform, false);
        var prt = _panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.18f, 0.15f);
        prt.anchorMax = new Vector2(0.82f, 0.85f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        _panel.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.97f);

        CreateCloseButton();
        _title = CreateTmp(_panel.transform, "图书馆自习", 22,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16f), new Vector2(400f, 36f),
            new Color(1f, 0.92f, 0.45f, 1f));
        _title.alignment = TextAlignmentOptions.Center;

        _status = CreateTmp(_panel.transform, "", 13,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -58f), new Vector2(560f, 52f),
            new Color(0.85f, 0.9f, 1f, 1f));
        _status.alignment = TextAlignmentOptions.TopLeft;
        _status.enableWordWrapping = true;
        _status.lineSpacing = 2f;

        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(_panel.transform, false);
        var srt = scrollGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 1f);
        srt.offsetMin = new Vector2(20f, 52f);
        srt.offsetMax = new Vector2(-20f, -128f);
        var scroll = scrollGo.AddComponent<ScrollRect>();
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollGo.transform, false);
        var vrt = viewport.AddComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = vrt.offsetMax = Vector2.zero;
        viewport.AddComponent<Image>().color = new Color(0.02f, 0.04f, 0.08f, 0.55f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        _listParent = content.AddComponent<RectTransform>();
        _listParent.anchorMin = new Vector2(0f, 1f);
        _listParent.anchorMax = new Vector2(1f, 1f);
        _listParent.pivot = new Vector2(0.5f, 1f);
        _listParent.anchoredPosition = Vector2.zero;
        _listParent.sizeDelta = new Vector2(0f, 0f);
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12;
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scroll.viewport = vrt;
        scroll.content = _listParent;
        scroll.vertical = true;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        var hint = CreateTmp(_panel.transform, "关闭：取消本次自习（不消耗活动）", 12,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 18f), new Vector2(520f, 24f),
            new Color(0.55f, 0.55f, 0.6f, 1f));
        hint.alignment = TextAlignmentOptions.Center;

        _panel.SetActive(false);
    }

    private void CreateCloseButton()
    {
        var closeGo = new GameObject("CloseBtn");
        closeGo.transform.SetParent(_panel.transform, false);
        var crt = closeGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(1f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-12f, -12f);
        crt.sizeDelta = new Vector2(96f, 32f);
        closeGo.AddComponent<Image>().color = new Color(0.38f, 0.28f, 0.28f, 1f);
        var btn = closeGo.AddComponent<Button>();
        btn.onClick.AddListener(CloseWithoutAction);
        var lblGo = new GameObject("Txt");
        lblGo.transform.SetParent(closeGo.transform, false);
        var lrt = lblGo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var tmp = lblGo.AddComponent<TextMeshProUGUI>();
        tmp.text = "关闭";
        tmp.fontSize = 14;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        ThustoryUIFont.Apply(tmp);
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string text, int size,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta, Color color)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(anchorMin.x + (anchorMax.x - anchorMin.x) * 0.5f, anchorMax.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        ThustoryUIFont.Apply(tmp);
        return tmp;
    }
}
