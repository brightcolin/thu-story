using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 活动结算全屏展示：成功时可选 behave 或 Resources/artTable 静图渐入 + 下方说明；失败时仅下方说明。
/// 仅鼠标左键点击遮罩、插图区或底部栏关闭。
/// </summary>
public class ActivityPresentationUI : MonoBehaviour
{
    public static ActivityPresentationUI Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    /// <summary>关闭动画结束、<see cref="IsOpen"/> 已为 false 后触发（缺餐条等可此时补弹）。</summary>
    public static event System.Action FullyClosed;

    [Serializable]
    public class ArtEntry
    {
        public string activityIdOrKey;
        public Sprite sprite;
    }

    [Header("插图映射（behave 失败时的备用图，Resources/ActivityPresentation/ 同名）")]
    public ArtEntry[] artTable;

    [Header("动效")]
    public float fadeInSeconds = 0.5f;
    public float fadeOutSeconds = 0.35f;

    private Canvas _canvas;
    private CanvasGroup _rootGroup;
    private Image _dim;
    private CanvasGroup _illustrationGroup;
    private RectTransform _illustrationRt;
    private Image _spriteFill;
    private CanvasGroup _bottomGroup;
    private TMP_Text _titleText;
    private TMP_Text _bodyText;
    private TMP_Text _hintText;
    private playercontrol _player;
    private bool _closing;
    private Coroutine _fadeCoroutine;

    private Transform _behaveOrigParent;
    private int _behaveOrigSibling;
    private bool _behaveOrigActive;
    private bool _behaveAttached;

    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("ActivityPresentationUI");
        DontDestroyOnLoad(go);
        go.AddComponent<ActivityPresentationUI>();
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
        _canvas.gameObject.SetActive(false);
        IsOpen = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RequestCloseFromUi() => StartClose();

    private void BuildUi()
    {
        var canvasGo = new GameObject("ActivityPresentationCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 320;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140, 600);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        _rootGroup = canvasGo.AddComponent<CanvasGroup>();
        _rootGroup.blocksRaycasts = true;
        _rootGroup.interactable = true;

        _dim = CreateFullRectImage(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
        _dim.raycastTarget = true;
        var dimSink = _dim.gameObject.AddComponent<ActivityPresentationPointerSink>();
        dimSink.closeOnLeftClick = true;

        var illusGo = new GameObject("IllustrationArea");
        illusGo.transform.SetParent(canvasGo.transform, false);
        _illustrationRt = illusGo.AddComponent<RectTransform>();
        _illustrationRt.anchorMin = new Vector2(0.5f, 0.55f);
        _illustrationRt.anchorMax = new Vector2(0.5f, 0.55f);
        _illustrationRt.pivot = new Vector2(0.5f, 0.5f);
        _illustrationRt.anchoredPosition = Vector2.zero;
        _illustrationRt.sizeDelta = new Vector2(720f, 405f);
        _illustrationGroup = illusGo.AddComponent<CanvasGroup>();

        var hitPlate = CreateFullRectImage(_illustrationRt, "IllustrationHit", new Color(0f, 0f, 0f, 0.02f));
        hitPlate.raycastTarget = true;
        var hitSink = hitPlate.gameObject.AddComponent<ActivityPresentationPointerSink>();
        hitSink.closeOnLeftClick = true;

        var spriteGo = new GameObject("SpriteFill");
        spriteGo.transform.SetParent(_illustrationRt, false);
        var spriteRt = spriteGo.AddComponent<RectTransform>();
        StretchFull(spriteRt);
        _spriteFill = spriteGo.AddComponent<Image>();
        _spriteFill.preserveAspect = true;
        _spriteFill.color = Color.white;
        _spriteFill.raycastTarget = false;

        var bottomGo = new GameObject("BottomPanel");
        bottomGo.transform.SetParent(canvasGo.transform, false);
        var bottomRt = bottomGo.AddComponent<RectTransform>();
        bottomRt.anchorMin = new Vector2(0.06f, 0f);
        bottomRt.anchorMax = new Vector2(0.94f, 0f);
        bottomRt.pivot = new Vector2(0.5f, 0f);
        bottomRt.anchoredPosition = new Vector2(0f, 10f);
        bottomRt.sizeDelta = new Vector2(0f, 220f);
        var bottomImg = bottomGo.AddComponent<Image>();
        bottomImg.color = new Color(0.05f, 0.07f, 0.12f, 0.94f);
        bottomImg.raycastTarget = true;
        var bottomSink = bottomGo.AddComponent<ActivityPresentationPointerSink>();
        bottomSink.closeOnLeftClick = true;
        _bottomGroup = bottomGo.AddComponent<CanvasGroup>();

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(bottomGo.transform, false);
        var tRt = titleGo.AddComponent<RectTransform>();
        tRt.anchorMin = new Vector2(0f, 1f);
        tRt.anchorMax = new Vector2(1f, 1f);
        tRt.pivot = new Vector2(0.5f, 1f);
        tRt.sizeDelta = new Vector2(-32f, 44f);
        tRt.anchoredPosition = new Vector2(0f, -6f);
        _titleText = titleGo.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = 22;
        _titleText.color = new Color(1f, 0.92f, 0.45f);
        _titleText.alignment = TextAlignmentOptions.MidlineLeft;
        _titleText.enableWordWrapping = true;
        _titleText.overflowMode = TextOverflowModes.Ellipsis;

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(bottomGo.transform, false);
        var bRt = bodyGo.AddComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(16f, 36f);
        bRt.offsetMax = new Vector2(-16f, -52f);
        _bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        _bodyText.fontSize = 15;
        _bodyText.color = Color.white;
        _bodyText.alignment = TextAlignmentOptions.TopLeft;
        _bodyText.enableWordWrapping = true;
        _bodyText.lineSpacing = 2f;
        _bodyText.overflowMode = TextOverflowModes.Overflow;

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(bottomGo.transform, false);
        var hRt = hintGo.AddComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0f, 0f);
        hRt.anchorMax = new Vector2(1f, 0f);
        hRt.pivot = new Vector2(0.5f, 0f);
        hRt.sizeDelta = new Vector2(-32f, 28f);
        hRt.anchoredPosition = new Vector2(0f, 6f);
        _hintText = hintGo.AddComponent<TextMeshProUGUI>();
        _hintText.fontSize = 13;
        _hintText.color = new Color(0.55f, 0.58f, 0.65f);
        _hintText.alignment = TextAlignmentOptions.MidlineRight;
        _hintText.text = "鼠标左键关闭";

        ThustoryUIFont.Apply(_titleText);
        ThustoryUIFont.Apply(_bodyText);
        ThustoryUIFont.Apply(_hintText);

        _illustrationRt.gameObject.SetActive(false);
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Image CreateFullRectImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    /// <param name="behaveTrigger">若非空则优先使用；否则按 <paramref name="activityId"/> 解析。</param>
    /// <param name="useBehavePresentation">为 false 时仅用静图（Resources/artTable），不挂载场景 behave，可避免 Overlay 下动画白屏等问题。</param>
    public void ShowSuccess(string activityId, string artKey, string title, string body, string behaveTrigger = null,
        bool useBehavePresentation = true)
    {
        string trig = behaveTrigger;
        if (string.IsNullOrEmpty(trig))
            trig = ActivityBehaveTriggerMap.Resolve(activityId);
        if (string.IsNullOrEmpty(trig) && !string.IsNullOrEmpty(artKey) && (artKey == "pic1" || artKey == "pic2"))
            trig = artKey;

        Sprite sp = ResolveSprite(activityId, artKey);
        ShowsuccessInternal(true, trig, sp, title, body, useBehavePresentation);
    }

    public void ShowFailure(string title, string message, string activityIdForUnlockHint = null)
    {
        string body = ActivityUnlockHints.AppendUnlockHint(message, activityIdForUnlockHint);
        ShowsuccessInternal(false, null, null, title, body, false);
    }

    private void ShowsuccessInternal(bool successWithArt, string behaveTrigger, Sprite sprite, string title, string body,
        bool useBehavePresentation)
    {
        StopFade();
        ReleaseBehaveIfAttached();

        _player = FindObjectOfType<playercontrol>();
        if (_player != null)
            _player.canmove = false;

        _titleText.text = title ?? "";
        _bodyText.text = body ?? "";
        _closing = false;

        bool mountedBehave = false;
        if (useBehavePresentation && successWithArt && _player != null && !string.IsNullOrEmpty(behaveTrigger))
        {
            _illustrationRt.gameObject.SetActive(true);
            mountedBehave = _player.TryAttachBehaveForPresentation(_illustrationRt, behaveTrigger,
                out _behaveOrigParent, out _behaveOrigSibling, out _behaveOrigActive);
            _behaveAttached = mountedBehave;
        }

        bool showSprite = successWithArt && !mountedBehave && sprite != null;
        _spriteFill.gameObject.SetActive(showSprite);
        if (showSprite)
        {
            _spriteFill.sprite = sprite;
            _illustrationRt.gameObject.SetActive(true);
        }

        if (!successWithArt || (!mountedBehave && !showSprite))
        {
            if (!mountedBehave)
                _illustrationRt.gameObject.SetActive(false);
        }

        bool fadeIllus = successWithArt && (mountedBehave || showSprite);
        if (_spriteFill != null && mountedBehave)
        {
            _spriteFill.gameObject.SetActive(false);
            _spriteFill.sprite = null;
        }

        _canvas.gameObject.SetActive(true);
        IsOpen = true;
        _rootGroup.alpha = 0f;
        _bottomGroup.alpha = 0f;
        if (_illustrationGroup != null)
            _illustrationGroup.alpha = fadeIllus ? 0f : 1f;

        _fadeCoroutine = StartCoroutine(FadeInRoutine(fadeIllus));
    }

    private IEnumerator FadeInRoutine(bool hasArt)
    {
        float dur = Mathf.Max(0.05f, fadeInSeconds);
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / dur);
            _rootGroup.alpha = u;
            _bottomGroup.alpha = u;
            if (hasArt && _illustrationGroup != null)
                _illustrationGroup.alpha = u;
            yield return null;
        }
        _rootGroup.alpha = 1f;
        _bottomGroup.alpha = 1f;
        if (hasArt && _illustrationGroup != null)
            _illustrationGroup.alpha = 1f;
        _fadeCoroutine = null;
    }

    private void StartClose()
    {
        if (_closing) return;
        _closing = true;
        StopFade();
        _fadeCoroutine = StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        float dur = Mathf.Max(0.05f, fadeOutSeconds);
        float a0 = _rootGroup.alpha;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float u = 1f - Mathf.Clamp01(t / dur);
            float a = a0 * u;
            _rootGroup.alpha = a;
            _bottomGroup.alpha = a;
            if (_illustrationRt != null && _illustrationRt.gameObject.activeSelf && _illustrationGroup != null)
                _illustrationGroup.alpha = a;
            yield return null;
        }

        ReleaseBehaveIfAttached();

        _canvas.gameObject.SetActive(false);
        IsOpen = false;
        FullyClosed?.Invoke();
        _closing = false;
        if (_player != null)
            _player.canmove = true;
        _player = null;
        _fadeCoroutine = null;
    }

    private void ReleaseBehaveIfAttached()
    {
        if (!_behaveAttached) return;
        var pc = _player != null ? _player : FindObjectOfType<playercontrol>();
        pc?.DetachBehaveFromPresentation(_behaveOrigParent, _behaveOrigSibling, _behaveOrigActive);
        _behaveAttached = false;
        _behaveOrigParent = null;
    }

    private void StopFade()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }

    private Sprite ResolveSprite(string activityId, string artKey)
    {
        if (artTable != null)
        {
            foreach (var e in artTable)
            {
                if (e?.sprite == null || string.IsNullOrEmpty(e.activityIdOrKey)) continue;
                if (e.activityIdOrKey == activityId || e.activityIdOrKey == artKey)
                    return e.sprite;
            }
        }
        if (!string.IsNullOrEmpty(activityId))
        {
            var a = Resources.Load<Sprite>($"ActivityPresentation/{activityId}");
            if (a != null) return a;
        }
        if (!string.IsNullOrEmpty(artKey))
        {
            var b = Resources.Load<Sprite>($"ActivityPresentation/{artKey}");
            if (b != null) return b;
        }
        return null;
    }
}
