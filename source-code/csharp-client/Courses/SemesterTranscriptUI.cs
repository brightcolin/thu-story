using System;
using System.Collections;
using System.Text;
using QinghuaStory;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 学期结束时全屏黑屏过渡到成绩单；隐藏 HUD，鼠标左键关闭后触发新学期回调（选课等）。
/// </summary>
public class SemesterTranscriptUI : MonoBehaviour
{
    const float FadeSeconds = 0.55f;
    const int CanvasSortOrder = 450;

    Canvas _canvas;
    CanvasGroup _group;
    Image _fade;
    TextMeshProUGUI _body;
    TextMeshProUGUI _hint;
    Coroutine _routine;
    Action _onDismiss;

    bool _hidHud;
    bool _playerFrozen;
    bool _savedCanMove;
    playercontrol _pc;
    bool _closeRequested;

    public static float MasteryToGradePoint(float mastery)
    {
        if (mastery >= 95f) return 4.0f;
        if (mastery >= 90f) return 3.7f;
        if (mastery >= 85f) return 3.3f;
        if (mastery >= 80f) return 3.0f;
        if (mastery >= 75f) return 2.7f;
        if (mastery >= 70f) return 2.3f;
        if (mastery >= 65f) return 2.0f;
        if (mastery >= 60f) return 1.5f;
        return 0f;
    }

    void Awake()
    {
        BuildUi();
        _canvas.gameObject.SetActive(false);
    }

    /// <param name="endedSemesterIndex">刚结束的学期序号（与 PlayerStatsData.semester_index 一致，0=大一上）。</param>
    public void ShowForEndedSemester(int endedSemesterIndex, int newSemesterIndex, string newSemesterName,
        float cumulativeGpa, Action onDismiss)
    {
        if (_routine != null) StopCoroutine(_routine);
        _closeRequested = false;
        _onDismiss = onDismiss;

        if (_body != null)
        {
            var mine = MyCoursesSnapshotCache.ResolveForEndedSemester(endedSemesterIndex);
            _body.text = BuildTranscriptText(endedSemesterIndex, newSemesterIndex, newSemesterName, cumulativeGpa, mine);
        }

        _canvas.gameObject.SetActive(true);
        _group.alpha = 0f;
        if (_fade != null)
        {
            var bc = _fade.color;
            bc.a = 0f;
            _fade.color = bc;
        }
        _group.interactable = false;
        _group.blocksRaycasts = true;

        _hidHud = GameHUD.Instance != null;
        if (_hidHud)
            GameHUD.Instance.SetHudVisible(false);

        _pc = FindObjectOfType<playercontrol>();
        if (_pc != null)
        {
            _playerFrozen = true;
            _savedCanMove = _pc.canmove;
            _pc.canmove = false;
        }

        ServerPauseCoordinator.Acquire(this);
        _routine = StartCoroutine(TranscriptRoutine());
    }

    IEnumerator TranscriptRoutine()
    {
        yield return FadeRoutine(0f, 1f, FadeSeconds);
        _group.interactable = true;

        while (!_closeRequested)
            yield return null;

        _group.interactable = false;
        yield return FadeRoutine(1f, 0f, FadeSeconds);
        Dismiss();
    }

    void Dismiss()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        _canvas.gameObject.SetActive(false);

        if (_playerFrozen && _pc != null)
        {
            _pc.canmove = _savedCanMove;
            _playerFrozen = false;
            _pc = null;
        }

        ServerPauseCoordinator.Release(this);

        if (_hidHud && GameHUD.Instance != null)
            GameHUD.Instance.SetHudVisible(true);
        _hidHud = false;

        var cb = _onDismiss;
        _onDismiss = null;
        cb?.Invoke();
    }

    internal void RequestCloseFromUserPointer()
    {
        if (_group != null && _group.interactable)
            _closeRequested = true;
    }

    static string BuildTranscriptText(int endedSemesterIndex, int newSemesterIndex, string newSemesterName,
        float cumulativeGpa, MyCoursesResponseV21 mine)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<size=130%><b>第 {endedSemesterIndex} 学期成绩单</b></size>");
        sb.AppendLine();
        sb.AppendLine("<b>课程名</b>　　　<b>课程掌握度</b>　　<b>学分绩</b>");
        sb.AppendLine();

        if (mine?.courses == null || mine.courses.Length == 0)
            sb.AppendLine("<color=#888888>（本学期无本地课程记录，请曾在本学期内拉取过「我的课程」）</color>");
        else
        {
            foreach (var c in mine.courses)
            {
                if (c == null) continue;
                float gp = MasteryToGradePoint(c.mastery);
                string name = string.IsNullOrEmpty(c.course_name) ? c.course_id : c.course_name;
                sb.AppendLine($"{name}　　　{c.mastery:F0}　　　{gp:F1}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("————————————————");
        sb.AppendLine($"<b>总绩点（累计）：{cumulativeGpa:F2}</b>");
        sb.AppendLine();
        string nextName = string.IsNullOrEmpty(newSemesterName) ? $"学期 {newSemesterIndex}" : newSemesterName;
        sb.AppendLine($"即将进入：{nextName}");
        return sb.ToString();
    }

    IEnumerator FadeRoutine(float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(Mathf.Lerp(from, to, duration > 0f ? t / duration : 1f));
            if (_fade != null)
            {
                var c = _fade.color;
                c.a = _group.alpha;
                _fade.color = c;
            }
            yield return null;
        }
        _group.alpha = to;
        if (_fade != null)
        {
            var c = _fade.color;
            c.a = to;
            _fade.color = c;
        }
    }

    void BuildUi()
    {
        var root = new GameObject("SemesterTranscriptRoot");
        root.transform.SetParent(transform, false);

        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortOrder;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1140f, 600f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        _group = root.AddComponent<CanvasGroup>();

        var bgGo = new GameObject("Black");
        bgGo.transform.SetParent(root.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        _fade = bgGo.AddComponent<Image>();
        _fade.color = new Color(0f, 0f, 0f, 1f);
        _fade.raycastTarget = true;
        var sink = bgGo.AddComponent<SemesterTranscriptPointerSink>();
        sink.Initialize(this);

        var textGo = new GameObject("TranscriptText");
        textGo.transform.SetParent(root.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.08f, 0.12f);
        textRt.anchorMax = new Vector2(0.92f, 0.88f);
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        _body = textGo.AddComponent<TextMeshProUGUI>();
        _body.fontSize = 20;
        _body.color = new Color(0.95f, 0.95f, 0.92f, 1f);
        _body.alignment = TextAlignmentOptions.TopLeft;
        _body.lineSpacing = 4f;
        _body.raycastTarget = false;
        ThustoryUIFont.Apply(_body);

        var hintGo = new GameObject("Hint");
        hintGo.transform.SetParent(root.transform, false);
        var hintRt = hintGo.AddComponent<RectTransform>();
        hintRt.anchorMin = new Vector2(0.1f, 0.02f);
        hintRt.anchorMax = new Vector2(0.9f, 0.1f);
        hintRt.offsetMin = hintRt.offsetMax = Vector2.zero;
        _hint = hintGo.AddComponent<TextMeshProUGUI>();
        _hint.text = "<i>按鼠标左键关闭成绩单，下学期正式开始</i>";
        _hint.fontSize = 16;
        _hint.color = new Color(0.75f, 0.75f, 0.8f, 1f);
        _hint.alignment = TextAlignmentOptions.Bottom;
        _hint.raycastTarget = false;
        ThustoryUIFont.Apply(_hint);
    }
}

/// <summary>
/// 全屏底图仅接收鼠标左键（走 EventSystem，避免 TMP 挡射线或旧 Input 与 UI 模块不同步导致无效）。
/// </summary>
[DisallowMultipleComponent]
internal sealed class SemesterTranscriptPointerSink : MonoBehaviour, IPointerDownHandler
{
    SemesterTranscriptUI _host;

    public void Initialize(SemesterTranscriptUI host) => _host = host;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_host == null) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;
        _host.RequestCloseFromUserPointer();
    }
}
