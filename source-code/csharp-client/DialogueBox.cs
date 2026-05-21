using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 对话框 - 底部显示，左侧头像、右侧文字
/// 左键点击推进到下一条，全部展示完自动关闭
/// </summary>
public class DialogueBox : MonoBehaviour
{
    [Serializable]
    public class DialogueEntry
    {
        public Sprite avatar;
        [TextArea(2, 4)]
        public string text;
    }

    [Header("外观")]
    [Tooltip("对话框背景图，留空用纯色")]
    public Sprite boxSprite;
    public Color boxPlaceholderColor = new Color(0.15f, 0.12f, 0.1f, 0.95f);
    [Range(0.2f, 0.5f)]
    public float boxHeightRatio = 0.25f;
    public float avatarSize = 80f;
    public int textFontSize = 20;
    public Color textColor = Color.white;

    public static DialogueBox Instance { get; private set; }

    private Canvas _canvas;
    private GameObject _panel;
    private Image _bgImage;
    private Image _avatarImage;
    private TextMeshProUGUI _textComp;
    private List<DialogueEntry> _entries;
    private int _index;
    private Action _onComplete;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _canvas = GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 150;
        _canvas.enabled = false;

        if (GetComponent<CanvasScaler>() == null)
        {
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1140, 600);  // 与 Fixed1140x600Camera 一致
            scaler.matchWidthOrHeight = 0.5f;
        }
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        CreatePanel();
    }

    private void CreatePanel()
    {
        _panel = new GameObject("DialogPanel");
        _panel.transform.SetParent(transform, false);

        var rect = _panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(0.5f, 0);
        rect.anchoredPosition = Vector2.zero;
        float h = Screen.height * boxHeightRatio;
        rect.sizeDelta = new Vector2(0, h);

        _bgImage = _panel.AddComponent<Image>();
        _bgImage.color = boxPlaceholderColor;
        if (boxSprite != null) _bgImage.sprite = boxSprite;

        var avatarObj = new GameObject("Avatar");
        avatarObj.transform.SetParent(_panel.transform, false);
        var avatarRect = avatarObj.AddComponent<RectTransform>();
        avatarRect.anchorMin = new Vector2(0, 0.5f);
        avatarRect.anchorMax = new Vector2(0, 0.5f);
        avatarRect.pivot = new Vector2(0, 0.5f);
        avatarRect.anchoredPosition = new Vector2(20, 0);
        avatarRect.sizeDelta = new Vector2(avatarSize, avatarSize);
        _avatarImage = avatarObj.AddComponent<Image>();
        _avatarImage.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(_panel.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = new Vector2(avatarSize + 40, 15);
        textRect.offsetMax = new Vector2(-20, -15);
        _textComp = textObj.AddComponent<TextMeshProUGUI>();
        _textComp.fontSize = textFontSize;
        _textComp.color = textColor;
        _textComp.alignment = TextAlignmentOptions.TopLeft;
        ThustoryUIFont.Apply(_textComp);
    }

    private void Update()
    {
        if (!_canvas.enabled || _entries == null || _index >= _entries.Count) return;
        if (Input.GetMouseButtonDown(0))
        {
            Advance();
        }
    }

    /// <summary>
    /// 显示对话，左键推进，完成后调用 onComplete
    /// </summary>
    public void Show(List<DialogueEntry> entries, Action onComplete = null)
    {
        if (entries == null || entries.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }
        APIManager.EnsureExists();
        // 静态对话不暂停服务端时钟（与 AI 对话一致）
        _entries = entries;
        _index = 0;
        _onComplete = onComplete;
        _canvas.enabled = true;
        DisplayCurrent();
    }

    public void Hide()
    {
        _canvas.enabled = false;
        _entries = null;
        _index = 0;
    }

    private void Advance()
    {
        _index++;
        if (_index >= _entries.Count)
        {
            var cb = _onComplete;
            _onComplete = null;
            Hide();
            cb?.Invoke();
            return;
        }
        DisplayCurrent();
    }

    private void DisplayCurrent()
    {
        var e = _entries[_index];
        _avatarImage.sprite = e.avatar;
        _avatarImage.enabled = e.avatar != null;
        _avatarImage.color = e.avatar != null ? Color.white : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        _textComp.text = e.text ?? "";
    }
}
