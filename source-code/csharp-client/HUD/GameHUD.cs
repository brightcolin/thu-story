using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 游戏抬头显示（HUD）。
/// 挂在 GameManager 上，自动在屏幕顶部/角落创建状态显示。
/// 不依赖任何预制体，完全代码创建。
/// </summary>
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("字体")]
    public TMP_FontAsset hudFont;

    [Header("外观")]
    public bool showHUD = true;
    public Color hudBgColor   = new Color(0f, 0f, 0f, 0.72f);
    public Color labelColor   = new Color(0.85f, 0.85f, 0.85f, 1f);
    public Color warningColor = new Color(1f, 0.35f, 0.3f, 1f);

    // ── 运行时 UI 引用 ──
    private Canvas _hudCanvas;
    private TMP_Text _gpaText, _energyText, _healthText, _timeText, _endingHintText;
    private Image _energyFill, _healthFill;

    // 通知横幅
    private GameObject _notifyPanel;
    private TMP_Text _notifyText;
    private Coroutine _notifyCoroutine;

    // 好感度小图标区
    private readonly Dictionary<string, TMP_Text> _friendshipLabels = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent != null)
        {
            var holder = new GameObject("Persistent_GameHUD");
            DontDestroyOnLoad(holder);
            transform.SetParent(holder.transform, true);
        }
        else
            DontDestroyOnLoad(gameObject);

        MealMissUIPanel.EnsureExists(hudFont);
        if (showHUD) BuildHUD();
    }

    // ══════════════════════════════════════════
    // 对外接口
    // ══════════════════════════════════════════

    public void RefreshStats(PlayerStatsData s)
    {
        if (!showHUD) return;

        if (_gpaText != null)
        {
            _gpaText.text = s.FormatGpaHudLine();
            if (s.GpaUiDeferredFirstSemester)
                _gpaText.color = labelColor;
            else
                _gpaText.color = s.gpa >= 3.5f ? new Color(0.4f, 1f, 0.4f) :
                                  s.gpa >= 2.6f ? labelColor :
                                  s.gpa >= 2.0f ? Color.yellow : warningColor;
        }
        if (_energyText != null) _energyText.text = $"⚡{s.energy}";
        if (_healthText  != null) _healthText.text  = $"❤️{s.health}";
        if (_energyFill  != null) _energyFill.fillAmount  = s.energy  / 100f;
        if (_healthFill   != null) _healthFill.fillAmount  = s.health   / 100f;
        if (_energyFill   != null) _energyFill.color  = BarColor(s.energy);
        if (_healthFill   != null) _healthFill.color   = BarColor(s.health);

        if (_timeText != null)
        {
            if (!string.IsNullOrEmpty(s.server_date_display) && !string.IsNullOrEmpty(s.server_time_display))
                _timeText.text = $"{s.server_date_display} · {s.server_time_display}";
            else
            {
                string wk = !string.IsNullOrEmpty(s.server_week_name) ? s.server_week_name : WeekName(s.current_week);
                string ph = !string.IsNullOrEmpty(s.server_phase_name) ? s.server_phase_name : PhaseName(s.current_phase);
                _timeText.text = $"{wk} · {ph}";
            }
        }

        if (_endingHintText != null)
            _endingHintText.text = GetEndingHint(s);
    }

    public void RefreshFriendship(string npcId, int value)
    {
        if (_friendshipLabels.TryGetValue(npcId, out var lbl))
        {
            string name = NpcShortName(npcId);
            lbl.text  = $"{name}\n{value}";
            lbl.color = value >= 80 ? new Color(1f, 0.6f, 0.8f) :
                         value >= 60 ? new Color(1f, 0.85f, 0.3f) :
                         value >= 30 ? new Color(0.6f, 0.9f, 1f) : labelColor;
        }
    }

    /// <summary>清空所有好感度显示（Restart 时调用）</summary>
    public void ClearFriendshipsDisplay()
    {
        foreach (var kv in _friendshipLabels)
        {
            string name = NpcShortName(kv.Key);
            kv.Value.text = $"{name}\n--";
            kv.Value.color = labelColor;
        }
    }

    /// <summary>成绩单等全屏层开启时隐藏 HUD，关闭后再显示。</summary>
    public void SetHudVisible(bool visible)
    {
        if (_hudCanvas != null)
            _hudCanvas.gameObject.SetActive(visible);
        MealMissUIPanel.Instance?.SetCanvasActive(visible);
    }

    public void ShowNotification(string message, float duration = 3f)
    {
        if (_notifyPanel == null) return;
        if (_notifyText != null) _notifyText.text = message;
        _notifyPanel.SetActive(true);

        if (_notifyCoroutine != null) StopCoroutine(_notifyCoroutine);
        _notifyCoroutine = StartCoroutine(HideNotifyAfter(duration));
    }

    /// <summary>转发至 <see cref="MealMissUIPanel"/>（缺餐 UI 独立组件）。</summary>
    public void ShowMealMissReminder(string[] missedMeals, int energyDelta, int healthDelta)
    {
        MealMissUIPanel.EnsureExists(hudFont);
        MealMissUIPanel.Instance?.Show(missedMeals, energyDelta, healthDelta);
    }

    public void HideMealMissReminder()
    {
        MealMissUIPanel.Instance?.Hide();
    }

    // ══════════════════════════════════════════
    // UI 构建
    // ══════════════════════════════════════════

    private void BuildHUD()
    {
        var canvasGo = new GameObject("GameHUDCanvas");
        DontDestroyOnLoad(canvasGo);
        _hudCanvas = canvasGo.AddComponent<Canvas>();
        _hudCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _hudCanvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 600);  // 与 Fixed1140x600Camera 一致
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 顶部状态栏 ──
        var topBar = CreateRect("TopBar", canvasGo.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -36f), new Vector2(0f, 0f));
        var topImg = topBar.gameObject.AddComponent<Image>();
        topImg.color = hudBgColor;

        float x = 16f;

        // GPA（第一学期显示「暂无」）
        _gpaText = CreateLabel(topBar.transform, "GPA --", x, -18f, 14, new Color(0.4f, 1f, 0.5f));
        _gpaText.rectTransform.sizeDelta = new Vector2(248f, 32f);
        x += 258f;

        // 精力条
        _energyText = CreateLabel(topBar.transform, "⚡60", x, -18f, 14, labelColor);
        x += 44f;
        _energyFill = CreateProgressBar(topBar.transform, "EnergyBar", x, -18f, 80f, 10f, new Color(0.3f, 0.9f, 0.3f));
        x += 90f;

        // 健康条
        _healthText = CreateLabel(topBar.transform, "❤️60", x, -18f, 14, labelColor);
        x += 44f;
        _healthFill = CreateProgressBar(topBar.transform, "HealthBar", x, -18f, 80f, 10f, new Color(0.9f, 0.3f, 0.3f));
        x += 90f;

        // 时间
        _timeText = CreateLabel(topBar.transform, "大一 · 上午", x, -18f, 14, new Color(0.8f, 0.9f, 1f));
        x += 130f;

        // 结局预测
        _endingHintText = CreateLabel(topBar.transform, "", x, -18f, 12, new Color(0.9f, 0.8f, 0.5f));

        // ── 右侧好感度小面板 ──
        BuildFriendshipPanel(canvasGo.transform);

        // ── 通知横幅（屏幕上方，居中） ──
        BuildNotifyBanner(canvasGo.transform);

        // 初始刷新
        if (PlayerManager.Instance != null)
            RefreshStats(PlayerManager.Instance.stats);
    }

    private void BuildFriendshipPanel(Transform parent)
    {
        string[] npcIds   = { "lin_wanqing","chen_yiran","shen_xingci","li_juan","wang_yuxia","zhang_kunlin","zhao_xiao" };
        string[] shortcuts = { "林晚晴","陈奕然","沈星辞","李娟","王玉霞","张锟霖","赵晓" };

        float panelW = 56f;
        float panelH = npcIds.Length * 42f + 8f;

        var panel = CreateRect("FriendshipPanel", parent,
            new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-panelW, -36f - panelH), new Vector2(0f, -36f));
        panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        for (int i = 0; i < npcIds.Length; i++)
        {
            float yPos = -8f - i * 42f - 21f;
            var lbl = CreateLabel(panel.transform, shortcuts[i] + "\n--", panelW / 2f, yPos, 10, labelColor, true);
            _friendshipLabels[npcIds[i]] = lbl;
        }
    }

    private void BuildNotifyBanner(Transform parent)
    {
        _notifyPanel = new GameObject("NotifyBanner");
        _notifyPanel.transform.SetParent(parent, false);
        var rt = _notifyPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.15f, 1f); rt.anchorMax = new Vector2(0.85f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -42f);
        rt.sizeDelta = new Vector2(0f, 46f);
        var img = _notifyPanel.AddComponent<Image>();
        img.color = new Color(0.05f, 0.05f, 0.2f, 0.93f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(_notifyPanel.transform, false);
        var tRt = textGo.AddComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(12f, 4f); tRt.offsetMax = new Vector2(-12f, -4f);
        _notifyText = textGo.AddComponent<TextMeshProUGUI>();
        _notifyText.fontSize = 16; _notifyText.color = new Color(1f, 0.95f, 0.6f, 1f);
        _notifyText.alignment = TextAlignmentOptions.Center;
        _notifyText.enableWordWrapping = false;
        if (hudFont != null) _notifyText.font = hudFont;
        else ThustoryUIFont.Apply(_notifyText);

        _notifyPanel.SetActive(false);
    }

    // ══════════════════════════════════════════
    // 工厂方法
    // ══════════════════════════════════════════

    private static RectTransform CreateRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        return rt;
    }

    private TMP_Text CreateLabel(Transform parent, string text, float x, float y,
                                   int fontSize, Color color, bool centered = false)
    {
        var go = new GameObject("Label_" + text.Substring(0, Mathf.Min(6, text.Length)));
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot     = new Vector2(centered ? 0.5f : 0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(centered ? 52f : 120f, 32f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = fontSize; tmp.color = color;
        tmp.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        if (hudFont != null) tmp.font = hudFont;
        else ThustoryUIFont.Apply(tmp);
        return tmp;
    }

    private Image CreateProgressBar(Transform parent, string name,
                                     float x, float y, float w, float h, Color color)
    {
        // 背景
        var bgGo = new GameObject(name + "_BG");
        bgGo.transform.SetParent(parent, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 1f);
        bgRt.pivot     = new Vector2(0f, 0.5f);
        bgRt.anchoredPosition = new Vector2(x, y);
        bgRt.sizeDelta = new Vector2(w, h);
        bgGo.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        // 填充
        var fillGo = new GameObject(name + "_Fill");
        fillGo.transform.SetParent(bgGo.transform, false);
        var fillRt = fillGo.AddComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(1f, 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.type      = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 0.6f;
        fillImg.color = color;
        return fillImg;
    }

    // ══════════════════════════════════════════
    // 辅助
    // ══════════════════════════════════════════

    private IEnumerator HideNotifyAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        _notifyPanel?.SetActive(false);
    }

    private static Color BarColor(float val)
    {
        if (val >= 60) return new Color(0.25f, 0.85f, 0.25f);
        if (val >= 30) return new Color(0.95f, 0.75f, 0.1f);
        return new Color(0.9f, 0.2f, 0.2f);
    }

    private static string WeekName(int w) => w switch { 1 => "大一", 2 => "大二", 3 => "大三", 4 => "大四", _ => $"第{w}年" };
    private static string PhaseName(string p) => p switch { "Morning" => "上午", "Afternoon" => "下午", "Evening" => "晚上", "Night" => "深夜", _ => p };
    private static string NpcShortName(string id) => id switch
    {
        "lin_wanqing" => "林晚晴", "chen_yiran" => "陈奕然", "shen_xingci" => "沈星辞",
        "li_juan" => "李娟", "wang_yuxia" => "王玉霞", "zhang_kunlin" => "张锟霖",
        "zhao_xiao" => "赵晓", _ => id
    };

    private static string GetEndingHint(PlayerStatsData s)
    {
        if (s.failed_credits >= 20) return "⚠️退学风险";
        if (s.is_game_over_server) return "毕业/结局";
        if (s.GpaUiDeferredFirstSemester) return "";
        if (s.gpa < 2.0f) return "⚠️危险";
        if (s.gpa >= 3.8f && s.research_skill >= 5) return "→本校保研";
        if (s.gpa >= 3.5f && s.research_skill >= 4) return "→外校保研";
        if (s.gpa >= 3.2f && s.english_skill  >= 4) return "→出国留学";
        if (s.social_skill  >= 4 && s.research_skill >= 3) return "→创业";
        return "→就业";
    }
}
