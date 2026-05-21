using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QinghuaStory;

/// <summary>
/// 场景活动：通过 POST /activities/execute 执行服务端活动，成功后推进时间并刷新 /player。
/// activity_id 须与 GET /activities / v2.1 文档一致（如 eat_canteen、rest、study_library_morning）。
/// </summary>
public class ActivityTrigger : MonoBehaviour
{
    [Header("活动配置")]
    [Tooltip("服务端活动 id，例如 eat_canteen、rest、study_library_morning（上课请用 /class/attend，勿填 attend_class）")]
    public string activity_id = "eat_canteen";

    [Header("交互设置")]
    public float triggerRadius = 2.5f;
    public string promptText = "按 [F] 进行活动";
    public Transform playerTransform;

    [Header("流程")]
    [Tooltip("使用 v2.1 ExecuteActivity（时间由服务端推进，勿再 /time/advance）")]
    public bool useV21ActivityApi = true;

    [Tooltip("仅旧版 API 时有效：成功后是否 POST /time/advance")]
    public bool advanceTimeAfterSuccess = false;

    [Tooltip("活动 id 为 study_library_* 时传给后端的 course_id")]
    public string courseIdForSelfStudy = "";

    private GameObject _promptBubble;
    private TMP_Text _promptLabel;

    private void Start()
    {
        APIManager.EnsureExists();

        if (playerTransform == null)
        {
            var pObj = GameObject.Find("player") ?? GameObject.Find("Player");
            if (pObj != null) playerTransform = pObj.transform;
        }
        BuildPromptBubble();
    }

    private void Update()
    {
        bool nearby = IsPlayerNearby();
        bool overlayOpen = ActivityPresentationUI.IsOpen;
        _promptBubble?.SetActive(nearby && !overlayOpen);

        if (nearby && !overlayOpen && Input.GetKeyDown(KeyCode.F)
            && !NPCManager.ShouldSuppressGlobalHotkeys()
            && !Input.GetKey(KeyCode.W) && !Input.GetKey(KeyCode.A)
            && !Input.GetKey(KeyCode.S) && !Input.GetKey(KeyCode.D))
        {
            StartCoroutine(DoActivityFlow());
        }
    }

    private IEnumerator DoActivityFlow()
    {
        _promptBubble?.SetActive(false);

        if (APIManager.Instance == null)
        {
            APIManager.EnsureExists();
            if (APIManager.Instance == null)
            {
                ActivityPresentationUI.EnsureExists();
                ActivityPresentationUI.Instance.ShowFailure("活动失败", "APIManager 未初始化");
                while (ActivityPresentationUI.IsOpen) yield return null;
                yield break;
            }
        }

        ActivityPresentationUI.EnsureExists();
        var snap = PlayerStatsSnapshot.Capture();
        PlayerManager.SuppressActivityHudToasts = true;

        if (useV21ActivityApi)
        {
            bool isLib = activity_id != null && activity_id.StartsWith("study_library_", StringComparison.Ordinal);
            string cid = isLib ? courseIdForSelfStudy : "";

            TimeInfoV21 timeBeforeExecute = null;
            bool isEatCanteen = string.Equals(activity_id, "eat_canteen", StringComparison.Ordinal);
            if (isEatCanteen)
            {
                bool tDone = false;
                APIManager.Instance.GetTimeV21(t => { timeBeforeExecute = t; tDone = true; },
                    _ => { tDone = true; });
                while (!tDone) yield return null;
            }

            ActivityResultV21 resultV21 = null;
            string transportErrV21 = null;
            var flowV21 = ServerActivityFlow.RunV21(activity_id, cid,
                (r, e) => { resultV21 = r; transportErrV21 = e; },
                isEatCanteen ? timeBeforeExecute : null);
            while (flowV21.MoveNext()) yield return flowV21.Current;

            PlayerManager.SuppressActivityHudToasts = false;

            if (!string.IsNullOrEmpty(transportErrV21))
            {
                ActivityPresentationUI.Instance.ShowFailure("活动失败", transportErrV21);
                while (ActivityPresentationUI.IsOpen) yield return null;
                yield break;
            }

            if (resultV21 == null)
            {
                ActivityPresentationUI.Instance.ShowFailure("活动失败", "无响应");
                while (ActivityPresentationUI.IsOpen) yield return null;
                yield break;
            }

            if (!resultV21.success)
            {
                string msg = string.IsNullOrEmpty(resultV21.activity_name)
                    ? "当前无法进行该活动。"
                    : resultV21.activity_name;
                ActivityPresentationUI.Instance.ShowFailure("无法进行活动", msg, activity_id);
                while (ActivityPresentationUI.IsOpen) yield return null;
                yield break;
            }

            string title = string.IsNullOrEmpty(resultV21.activity_name)
                ? "活动完成"
                : resultV21.activity_name;
            string body = ActivityPresentationFormatter.FormatV21(resultV21, snap);
            string artKey = isLib ? "read" : activity_id;
            ActivityPresentationUI.Instance.ShowSuccess(activity_id, artKey, title, body, null, false);
            while (ActivityPresentationUI.IsOpen) yield return null;
            yield break;
        }

        ActivityExecuteResult result = null;
        string transportErr = null;
        var flow = ServerActivityFlow.Run(activity_id, advanceTimeAfterSuccess,
            (r, e) => { result = r; transportErr = e; });
        while (flow.MoveNext()) yield return flow.Current;

        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(transportErr))
        {
            ActivityPresentationUI.Instance.ShowFailure("活动失败", transportErr);
            while (ActivityPresentationUI.IsOpen) yield return null;
            yield break;
        }

        if (result == null)
        {
            ActivityPresentationUI.Instance.ShowFailure("活动失败", "无响应");
            while (ActivityPresentationUI.IsOpen) yield return null;
            yield break;
        }

        if (!result.success)
        {
            string msg = string.IsNullOrEmpty(result.message) ? "当前无法进行该活动。" : result.message;
            ActivityPresentationUI.Instance.ShowFailure(result.activity_name ?? "活动", msg, activity_id);
            while (ActivityPresentationUI.IsOpen) yield return null;
            yield break;
        }

        string titleOk = string.IsNullOrEmpty(result.activity_name) ? "活动完成" : result.activity_name;
        string bodyOk = ActivityPresentationFormatter.FormatLegacy(result, snap);
        ActivityPresentationUI.Instance.ShowSuccess(activity_id, activity_id, titleOk, bodyOk, null, false);
        while (ActivityPresentationUI.IsOpen) yield return null;
    }

    private void BuildPromptBubble()
    {
        var canvasGo = new GameObject("ActivityPromptCanvas_" + gameObject.name);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 90;
        canvasGo.AddComponent<GraphicRaycaster>();

        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = Vector3.up * 1.2f;
        canvasGo.transform.localScale = Vector3.one * 0.01f;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180f, 36f);

        var bg = canvasGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);

        var textGo = new GameObject("PromptText");
        textGo.transform.SetParent(canvasGo.transform, false);
        var tRt = textGo.AddComponent<RectTransform>();
        tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(6f, 4f); tRt.offsetMax = new Vector2(-6f, -4f);
        _promptLabel = textGo.AddComponent<TextMeshProUGUI>();
        _promptLabel.text = promptText;
        _promptLabel.fontSize = 14;
        _promptLabel.color = Color.white;
        _promptLabel.alignment = TextAlignmentOptions.Center;
        ThustoryUIFont.Apply(_promptLabel);

        _promptBubble = canvasGo;
        _promptBubble.SetActive(false);
    }

    private bool IsPlayerNearby()
    {
        if (playerTransform == null) return false;
        return Vector2.Distance(playerTransform.position, transform.position) <= triggerRadius;
    }
}
