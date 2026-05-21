using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 缺餐提示 UI：屏幕底部条，展示漏餐餐次与精力/健康变化，左键点击关闭。
/// 独立 Canvas（sortingOrder 340），高于活动结算层。
/// </summary>
public class MealMissUIPanel : MonoBehaviour
{
    public static MealMissUIPanel Instance { get; private set; }

    private static TMP_FontAsset s_fontFromHud;

    private Canvas _canvas;
    private GameObject _panelRoot;
    private TMP_Text _titleText;
    private TMP_Text _bodyText;
    private TMP_Text _hintText;

    /// <summary>由 <see cref="GameHUD"/> 启动时调用，创建常驻缺餐条。</summary>
    public static void EnsureExists(TMP_FontAsset hudFont = null)
    {
        if (Instance != null) return;
        s_fontFromHud = hudFont;
        var go = new GameObject("MealMissUIPanel");
        DontDestroyOnLoad(go);
        go.AddComponent<MealMissUIPanel>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        var font = s_fontFromHud;
        s_fontFromHud = null;
        Build(font);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Show(string[] missedMeals, int energyDelta, int healthDelta)
    {
        if (_panelRoot == null || missedMeals == null || missedMeals.Length == 0)
            return;

        var names = new List<string>(missedMeals.Length);
        foreach (var id in missedMeals)
        {
            if (string.IsNullOrEmpty(id)) continue;
            names.Add(MealIdToDisplayName(id));
        }
        if (names.Count == 0) return;

        if (_titleText != null)
            _titleText.text = "未按时用餐";
        if (_bodyText != null)
        {
            string mealsLine = string.Join("、", names);
            string en = energyDelta == 0 ? "±0" : (energyDelta > 0 ? $"+{energyDelta}" : $"{energyDelta}");
            string hp = healthDelta == 0 ? "±0" : (healthDelta > 0 ? $"+{healthDelta}" : $"{healthDelta}");
            _bodyText.text = $"{mealsLine}\n<color=#FFCC88>精力 {en}</color>    <color=#FF8888>健康 {hp}";
        }
        if (_hintText != null)
            _hintText.text = "鼠标左键点击关闭";

        _panelRoot.SetActive(true);
        _panelRoot.transform.SetAsLastSibling();
    }

     
    public void Hide()
    {
        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    /// <summary>成绩单等全屏层与主 HUD 一并隐藏时使用。</summary>
    public void SetCanvasActive(bool active)
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(active);
    }

    private void Build(TMP_FontAsset hudFont)
    {
        var canvasGo = new GameObject("MealMissOverlayCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 340;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 600);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        _panelRoot = new GameObject("MealMissReminder");
        _panelRoot.transform.SetParent(canvasGo.transform, false);
        var rt = _panelRoot.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.06f, 0f);
        rt.anchorMax = new Vector2(0.94f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 12f);
        rt.sizeDelta = new Vector2(0f, 116f);

        var img = _panelRoot.AddComponent<Image>();
        img.color = new Color(0.14f, 0.06f, 0.05f, 0.96f);
        var btn = _panelRoot.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(Hide);

        float pad = 14f;
        var stroke = new GameObject("AccentLine");
        stroke.transform.SetParent(_panelRoot.transform, false);
        var strokeRt = stroke.AddComponent<RectTransform>();
        strokeRt.anchorMin = new Vector2(0f, 1f);
        strokeRt.anchorMax = new Vector2(1f, 1f);
        strokeRt.pivot = new Vector2(0.5f, 1f);
        strokeRt.offsetMin = new Vector2(0f, -4f);
        strokeRt.offsetMax = new Vector2(0f, 0f);
        var strokeImg = stroke.AddComponent<Image>();
        strokeImg.color = new Color(0.95f, 0.42f, 0.22f, 1f);
        strokeImg.raycastTarget = false;

        var iconRow = new GameObject("IconStrip");
        iconRow.transform.SetParent(_panelRoot.transform, false);
        var iconRt = iconRow.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 1f);
        iconRt.anchorMax = new Vector2(0f, 1f);
        iconRt.pivot = new Vector2(0f, 1f);
        iconRt.anchoredPosition = new Vector2(pad, -8f);
        iconRt.sizeDelta = new Vector2(36f, 28f);
        var iconTxt = iconRow.AddComponent<TextMeshProUGUI>();
        iconTxt.fontSize = 22;
        iconTxt.alignment = TextAlignmentOptions.Center;
        iconTxt.text = "\u26A0";
        iconTxt.color = new Color(1f, 0.75f, 0.35f, 1f);
        iconTxt.raycastTarget = false;
        ApplyFont(iconTxt, hudFont);

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_panelRoot.transform, false);
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.anchoredPosition = new Vector2(pad + 40f, -8f);
        titleRt.sizeDelta = new Vector2(-pad * 2f - 44f, 26f);
        _titleText = titleGo.AddComponent<TextMeshProUGUI>();
        _titleText.text = "未按时用餐";
        _titleText.fontSize = 18;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = new Color(1f, 0.93f, 0.88f, 1f);
        _titleText.alignment = TextAlignmentOptions.Left;
        ApplyFont(_titleText, hudFont);
        _titleText.raycastTarget = false;

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(_panelRoot.transform, false);
        var bodyRt = bodyGo.AddComponent<RectTransform>();
        bodyRt.anchorMin = new Vector2(0f, 0f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.offsetMin = new Vector2(pad, 30f);
        bodyRt.offsetMax = new Vector2(-pad, -40f);
        _bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        _bodyText.fontSize = 15;
        _bodyText.color = new Color(0.96f, 0.96f, 0.96f, 1f);
        _bodyText.alignment = TextAlignmentOptions.TopLeft;
        _bodyText.lineSpacing = 4f;
        _bodyText.richText = true;
        ApplyFont(_bodyText, hudFont);
        _bodyText.raycastTarget = false;

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(_panelRoot.transform, false);
        var hintRt = hintGo.AddComponent<RectTransform>();
        hintRt.anchorMin = new Vector2(0f, 0f);
        hintRt.anchorMax = new Vector2(1f, 0f);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 8f);
        hintRt.sizeDelta = new Vector2(-pad * 2f, 18f);
        _hintText = hintGo.AddComponent<TextMeshProUGUI>();
        _hintText.fontSize = 12;
        _hintText.color = new Color(0.72f, 0.68f, 0.62f, 0.95f);
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.text = "鼠标左键点击关闭";
        ApplyFont(_hintText, hudFont);
        _hintText.raycastTarget = false;

        _panelRoot.SetActive(false);
    }

    private static void ApplyFont(TMP_Text tmp, TMP_FontAsset hudFont)
    {
        if (hudFont != null) tmp.font = hudFont;
        else ThustoryUIFont.Apply(tmp);
    }

    private static string MealIdToDisplayName(string id)
    {
        switch ((id ?? "").Trim().ToLowerInvariant())
        {
            case "breakfast": return "早餐";
            case "lunch": return "午餐";
            case "dinner": return "晚餐";
            default: return id;
        }
    }
}
