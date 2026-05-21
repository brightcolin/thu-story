using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 图书馆 R / 操场 R 等：展示 GET /activities 中的说明与预览，确认后由 <see cref="playercontrol.RunFixedActivityV21"/> 执行 v2.1 活动。
/// </summary>
public class SceneRActivityUI : MonoBehaviour
{
    public static SceneRActivityUI Instance { get; private set; }

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("SceneRActivityUI");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SceneRActivityUI>();
    }

    public static bool IsOpen => Instance != null && Instance._open;

    [Header("行为")]
    public bool pauseServerTimeWhileOpen = true;
    public bool disablePlayerMoveWhileOpen = true;

    private GameObject _canvasRoot;
    private GameObject _panel;
    private TMP_Text _title;
    private TMP_Text _body;
    private Button _btnStart;
    private Button _btnCancel;

    private playercontrol _pc;
    private bool _open;
    private bool _pauseHeld;

    private string _pendingActivityId;
    private string _pendingArtKey;
    private string _panelTitle;

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

    /// <param name="panelTitle">面板标题（如「社团活动」）</param>
    /// <param name="artKey">传入 <see cref="ActivityPresentationUI.ShowSuccess"/> 的插图键</param>
    public static void OpenForSceneActivity(playercontrol pc, string activityId, string panelTitle, string artKey)
    {
        EnsureExists();
        Instance.DoOpen(pc, activityId, panelTitle, artKey);
    }

    public static void CloseIfOpenForScenePolicy()
    {
        if (Instance == null || !Instance._open) return;
        Instance.CloseWithoutAction();
    }

    void DoOpen(playercontrol pc, string activityId, string panelTitle, string artKey)
    {
        if (_open) return;
        if (pc == null || string.IsNullOrEmpty(activityId)) return;

        _pc = pc;
        _pendingActivityId = activityId;
        _pendingArtKey = string.IsNullOrEmpty(artKey) ? activityId : artKey;
        _panelTitle = panelTitle ?? activityId;
        _open = true;

        if (_panel != null)
            _panel.SetActive(true);
        if (_title != null)
            _title.text = _panelTitle;
        if (_body != null)
            _body.text = "正在加载活动列表…";
        if (_btnStart != null)
            _btnStart.interactable = false;

        if (pauseServerTimeWhileOpen)
        {
            ServerPauseCoordinator.Acquire(this);
            _pauseHeld = true;
        }

        if (disablePlayerMoveWhileOpen && _pc != null)
            _pc.canmove = false;

        StartCoroutine(LoadActivitiesAndBind());
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

        _pc = null;
    }

    private void OnStartClicked()
    {
        if (!_open || _pc == null || _btnStart == null || !_btnStart.interactable) return;

        var host = _pc;
        var id = _pendingActivityId;
        var key = _pendingArtKey;
        var label = _panelTitle;

        CloseWithoutAction();
        host.StartCoroutine(host.RunFixedActivityV21(id, label, key));
    }

    private IEnumerator LoadActivitiesAndBind()
    {
        if (APIManager.Instance == null)
        {
            APIManager.EnsureExists();
            if (APIManager.Instance == null)
            {
                if (_body != null) _body.text = "APIManager 未初始化";
                yield break;
            }
        }

        ActivitiesResponseV21 resp = null;
        string err = null;
        bool done = false;
        APIManager.Instance.GetActivitiesV21(
            r => { resp = r; done = true; },
            e => { err = e; done = true; });
        while (!done) yield return null;

        if (!string.IsNullOrEmpty(err) || resp == null)
        {
            if (_body != null) _body.text = "加载失败：" + (err ?? "无响应");
            yield break;
        }

        ActivityInfoV21 info = null;
        if (resp.activities != null)
        {
            foreach (var a in resp.activities)
            {
                if (a != null && string.Equals(a.id, _pendingActivityId, StringComparison.Ordinal))
                {
                    info = a;
                    break;
                }
            }
        }

        if (info == null)
        {
            if (_body != null)
            {
                string baseMsg =
                    "当前时段不可进行：「" + _panelTitle + "」未出现在可执行活动列表中。\n" +
                    "请确认游戏内时间与后端开放条件，或稍后再试。";
                _body.text = ActivityUnlockHints.AppendUnlockHint(baseMsg, _pendingActivityId);
            }
            if (_btnStart != null)
                _btnStart.interactable = false;
            yield break;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(info.name))
            sb.AppendLine(info.name);
        if (!string.IsNullOrEmpty(info.description))
            sb.AppendLine(info.description.Trim());
        if (!string.IsNullOrEmpty(info.time_cost))
            sb.AppendLine("耗时：" + info.time_cost);
        string fx = FormatEffectPreview(info.effect_preview);
        if (!string.IsNullOrEmpty(fx))
            sb.AppendLine(fx.TrimEnd());
        if (sb.Length == 0)
            sb.AppendLine("（暂无详细说明）");

        if (_body != null)
            _body.text = sb.ToString().Trim();
        if (_btnStart != null)
            _btnStart.interactable = true;
    }

    static string FormatEffectPreview(ActivityEffectPreviewV21 p)
    {
        if (p == null) return "";
        var parts = new System.Collections.Generic.List<string>();
        if (p.energy != 0)
            parts.Add($"精力 {(p.energy > 0 ? "+" : "")}{p.energy}");
        if (p.health != 0)
            parts.Add($"健康 {(p.health > 0 ? "+" : "")}{p.health}");
        if (p.research_ability != 0)
            parts.Add($"科研 {(p.research_ability > 0 ? "+" : "")}{p.research_ability}");
        if (p.social_ability != 0)
            parts.Add($"社工 {(p.social_ability > 0 ? "+" : "")}{p.social_ability}");
        return parts.Count == 0 ? "" : "预计属性变化：" + string.Join("，", parts);
    }

    private void BuildUi()
    {
        _canvasRoot = new GameObject("SceneRActivityCanvas");
        _canvasRoot.transform.SetParent(transform, false);

        var canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 276;
        var scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 640);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRoot.AddComponent<GraphicRaycaster>();

        _panel = new GameObject("Panel");
        _panel.transform.SetParent(_canvasRoot.transform, false);
        var prt = _panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.22f, 0.2f);
        prt.anchorMax = new Vector2(0.78f, 0.8f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        _panel.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.97f);

        CreateCloseHeaderButton();

        _title = CreateTmp(_panel.transform, "活动", 22,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16f), new Vector2(400f, 36f),
            new Color(1f, 0.92f, 0.45f, 1f));
        _title.alignment = TextAlignmentOptions.Center;

        _body = CreateTmp(_panel.transform, "", 14,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20f), new Vector2(520f, 240f),
            new Color(0.85f, 0.9f, 1f, 1f));
        _body.alignment = TextAlignmentOptions.TopLeft;
        _body.enableWordWrapping = true;
        _body.lineSpacing = 2f;

        var btnRow = new GameObject("ButtonRow");
        btnRow.transform.SetParent(_panel.transform, false);
        var brt = btnRow.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0f);
        brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0, 56f);
        brt.sizeDelta = new Vector2(420f, 44f);

        _btnCancel = CreateFooterButton(btnRow.transform, "取消", new Vector2(-108f, 0), new Vector2(140f, 40f),
            new Color(0.35f, 0.32f, 0.32f, 1f), CloseWithoutAction);
        _btnStart = CreateFooterButton(btnRow.transform, "开始", new Vector2(108f, 0), new Vector2(140f, 40f),
            new Color(0.22f, 0.42f, 0.32f, 1f), OnStartClicked);

        var hint = CreateTmp(_panel.transform, "取消：不消耗活动", 12,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 14f), new Vector2(520f, 22f),
            new Color(0.55f, 0.55f, 0.6f, 1f));
        hint.alignment = TextAlignmentOptions.Center;

        _panel.SetActive(false);
    }

    private void CreateCloseHeaderButton()
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

    private static Button CreateFooterButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
        Color bg, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label + "Btn");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = bg;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        var lblGo = new GameObject("Txt");
        lblGo.transform.SetParent(go.transform, false);
        var lrt = lblGo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var tmp = lblGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 15;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        ThustoryUIFont.Apply(tmp);
        return btn;
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
