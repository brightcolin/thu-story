using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 课表查看：GET /courses/schedule，网格展示周一至周五 × 第1～4节（与《前端对接指南》§8.2 一致）。
/// </summary>
public class ScheduleViewUI : MonoBehaviour
{
    private static readonly string[] WeekdayLabels = { "周一", "周二", "周三", "周四", "周五" };
    private static readonly string[] PeriodTimeHints =
    {
        "第1节\n8:00-\n9:30",
        "第2节\n9:50-\n12:10",
        "第3节\n13:30-\n15:00",
        "第4节\n19:20-\n21:00"
    };

    [Header("快捷键")]
    public KeyCode toggleKey = KeyCode.U;

    [Header("UI（可留空则首次打开时生成）")]
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text footerText;
    public Button closeButton;
    [Tooltip("6×5 网格父物体；留空则由运行时创建")]
    public RectTransform gridParent;

    [Header("打开时行为")]
    public bool pauseServerTimeWhileOpen = false;
    public bool disablePlayerMoveWhileOpen = false;

    [Header("字体（与人物属性 / 选课 UI 一致）")]
    public TMP_FontAsset chineseFontAsset;

    private bool _runtimeBuilt;
    private TMP_FontAsset _cachedResolvedFont;
    private TMP_Text[,] _cells;
    private playercontrol _pc;
    private bool _open;
    private bool _pauseHeld;

    private void Awake()
    {
        APIManager.EnsureExists();
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);
    }

    private void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey) && !NPCManager.ShouldSuppressGlobalHotkeys())
            TogglePanel();
    }

    public void TogglePanel()
    {
        if (_open) ClosePanel();
        else OpenPanel();
    }

    public void OpenPanel()
    {
        EnsureUiExists();
        if (panelRoot != null)
            panelRoot.SetActive(true);
        _open = true;

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

        if (titleText != null)
            titleText.text = "我的课表";

        RefreshScheduleFromServer();
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
    }

    private void OnDisable()
    {
        if (_open)
            ClosePanel();
    }

    private void RefreshScheduleFromServer()
    {
        if (APIManager.Instance == null)
        {
            SetAllCellsError("无法连接后端");
            return;
        }

        APIManager.Instance.GetScheduleV21(
            data =>
            {
                if (!_open) return;
                FillGridFromSchedule(data);
                if (footerText != null)
                {
                    footerText.color = new Color(0.65f, 0.68f, 0.75f, 1f);
                    footerText.text = "数据来自服务端课表 · 按 " + (toggleKey != KeyCode.None ? toggleKey.ToString() : "关闭") + " 或关闭按钮退出";
                }
            },
            err =>
            {
                if (!_open) return;
                SetAllCellsError("加载失败：" + err);
            });
    }

    private void FillGridFromSchedule(ScheduleResponseV21 data)
    {
        var map = new Dictionary<(int day, int period), string>();
        if (data?.schedule != null)
        {
            foreach (var s in data.schedule)
            {
                if (s == null || string.IsNullOrEmpty(s.course_id)) continue;
                int d = Mathf.Clamp(s.day_of_week, 0, 4);
                int p = Mathf.Clamp(s.period, 1, 4);
                string name = string.IsNullOrEmpty(s.course_name) ? s.course_id : s.course_name;
                var key = (d, p);
                if (map.TryGetValue(key, out string existing))
                    map[key] = existing + " / " + name;
                else
                    map[key] = name;
            }
        }

        if (_cells == null) return;

        for (int period = 1; period <= 4; period++)
        {
            for (int day = 0; day <= 4; day++)
            {
                var cell = _cells[period - 1, day];
                if (cell == null) continue;
                if (map.TryGetValue((day, period), out string course))
                {
                    cell.text = course;
                    cell.color = new Color(0.95f, 0.97f, 1f, 1f);
                }
                else
                {
                    cell.text = "—";
                    cell.color = new Color(0.55f, 0.58f, 0.65f, 1f);
                }
            }
        }
    }

    private void SetAllCellsError(string msg)
    {
        if (footerText != null)
        {
            footerText.text = msg;
            footerText.color = new Color(1f, 0.55f, 0.5f, 1f);
        }
        if (_cells == null) return;
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                var cell = _cells[r, c];
                if (cell != null)
                {
                    cell.text = "—";
                    cell.color = new Color(0.55f, 0.58f, 0.65f, 1f);
                }
            }
        }
    }

    private void EnsureUiExists()
    {
        if (panelRoot != null && gridParent != null && _cells != null)
            return;

        if (panelRoot == null)
        {
            if (_runtimeBuilt) return;
            BuildRuntimeUi();
            _runtimeBuilt = true;
            return;
        }

        if (panelRoot != null && gridParent == null)
        {
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(panelRoot.transform, false);
            gridParent = gridGo.AddComponent<RectTransform>();
            gridParent.anchorMin = new Vector2(0f, 0.1f);
            gridParent.anchorMax = new Vector2(1f, 1f);
            gridParent.offsetMin = new Vector2(12f, 44f);
            gridParent.offsetMax = new Vector2(-12f, -48f);
        }

        if (gridParent != null && _cells == null)
            BuildGridCellsIfNeeded();

        if (panelRoot != null && footerText == null)
        {
            footerText = TmpLabel(panelRoot.transform, "", 11,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 10f), new Vector2(700, 28));
            footerText.alignment = TextAlignmentOptions.Center;
            footerText.color = new Color(0.65f, 0.68f, 0.75f, 1f);
        }
    }

    private void BuildRuntimeUi()
    {
        var canvasGo = new GameObject("ScheduleViewCanvas");
        DontDestroyOnLoad(canvasGo);

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 270;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 640);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        panelRoot = new GameObject("SchedulePanel");
        panelRoot.transform.SetParent(canvasGo.transform, false);
        var prt = panelRoot.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.1f, 0.12f);
        prt.anchorMax = new Vector2(0.9f, 0.9f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        panelRoot.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.97f);

        titleText = TmpLabel(panelRoot.transform, "我的课表", 22,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -10f), new Vector2(400, 36));

        var closeGo = new GameObject("CloseBtn");
        closeGo.transform.SetParent(panelRoot.transform, false);
        var crt = closeGo.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(1f, 1f);
        crt.anchorMax = new Vector2(1f, 1f);
        crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-8f, -8f);
        crt.sizeDelta = new Vector2(96, 30);
        closeGo.AddComponent<Image>().color = new Color(0.35f, 0.28f, 0.28f, 1f);
        closeButton = closeGo.AddComponent<Button>();
        closeButton.onClick.AddListener(ClosePanel);
        var closeLbl = TmpLabel(closeGo.transform, "关闭", 13, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        closeLbl.alignment = TextAlignmentOptions.Center;

        var gridGo = new GameObject("Grid");
        gridGo.transform.SetParent(panelRoot.transform, false);
        gridParent = gridGo.AddComponent<RectTransform>();
        gridParent.anchorMin = new Vector2(0f, 0.1f);
        gridParent.anchorMax = new Vector2(1f, 1f);
        gridParent.offsetMin = new Vector2(12f, 44f);
        gridParent.offsetMax = new Vector2(-12f, -48f);

        BuildGridCellsIfNeeded();

        footerText = TmpLabel(panelRoot.transform, "", 11,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 10f), new Vector2(700, 28));
        footerText.alignment = TextAlignmentOptions.Center;
        footerText.color = new Color(0.65f, 0.68f, 0.75f, 1f);

        panelRoot.SetActive(false);
    }

    private void BuildGridCellsIfNeeded()
    {
        if (gridParent == null) return;

        for (int i = gridParent.childCount - 1; i >= 0; i--)
            Destroy(gridParent.GetChild(i).gameObject);

        var glg = gridParent.GetComponent<GridLayoutGroup>();
        if (glg == null) glg = gridParent.gameObject.AddComponent<GridLayoutGroup>();
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 6;
        glg.cellSize = new Vector2(138f, 76f);
        glg.spacing = new Vector2(5f, 5f);
        glg.padding = new RectOffset(6, 6, 6, 6);
        glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment = TextAnchor.UpperLeft;

        _cells = new TMP_Text[4, 5];

        AddGridHeaderCell("节次/星期");
        for (int d = 0; d < 5; d++)
            AddGridHeaderCell(WeekdayLabels[d]);

        for (int r = 0; r < 4; r++)
        {
            AddGridHeaderCell(PeriodTimeHints[r]);
            for (int d = 0; d < 5; d++)
                _cells[r, d] = AddGridCourseCell();
        }
    }

    private void AddGridHeaderCell(string label)
    {
        var go = new GameObject("HeadCell");
        go.transform.SetParent(gridParent, false);
        go.AddComponent<Image>().color = new Color(0.14f, 0.19f, 0.28f, 1f);
        var tgo = new GameObject("Text");
        tgo.transform.SetParent(go.transform, false);
        var trt = tgo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(3f, 3f);
        trt.offsetMax = new Vector2(-3f, -3f);
        var tmp = tgo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 11;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;
        tmp.color = new Color(0.85f, 0.88f, 0.95f, 1f);
        ApplyChineseFontTo(tmp);
    }

    private TMP_Text AddGridCourseCell()
    {
        var go = new GameObject("CourseCell");
        go.transform.SetParent(gridParent, false);
        go.AddComponent<Image>().color = new Color(0.1f, 0.14f, 0.22f, 1f);
        var tgo = new GameObject("Text");
        tgo.transform.SetParent(go.transform, false);
        var trt = tgo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4f, 4f);
        trt.offsetMax = new Vector2(-4f, -4f);
        var tmp = tgo.AddComponent<TextMeshProUGUI>();
        tmp.text = "—";
        tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.Midline;
        tmp.enableWordWrapping = true;
        tmp.color = new Color(0.55f, 0.58f, 0.65f, 1f);
        ApplyChineseFontTo(tmp);
        return tmp;
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

    private TMP_FontAsset ResolveChineseFont()
    {
        if (chineseFontAsset != null)
            return chineseFontAsset;
        if (_cachedResolvedFont != null)
            return _cachedResolvedFont;

        if (titleText != null && titleText.font != null)
            _cachedResolvedFont = titleText.font;
        else if (footerText != null && footerText.font != null)
            _cachedResolvedFont = footerText.font;
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
}
