using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using QinghuaStory;

public class playercontrol:MonoBehaviour
{
    public GameObject scenectrl;
    private scenetrans tr;
    public Animator animator;
    private float moveSpeed = 5f;
    private int state = 3;
    private Vector2 movement,original;
    public Rigidbody2D environmentRb;
    public Rigidbody2D rb;
    private int lastscene = -1;
    private bool d=true, u=true, l=true, r=true;
    private float xmin=0f, xmax=0f, ymin=0f, ymax=0f, cameraHalfWidth=0f, cameraHalfHeight=0f;
    public GameObject[] back = new GameObject[14];
    public bool canmove = true;
    public GameObject behave;
    private Animator _behAnimator;
    private Graphic[] _behaveRaycastGraphics;
    private bool[] _behaveRaycastPrev;
    public GameObject time;

    [Header("场景活动（v2.1：id 须与 GET /activities 一致；留空则按当前游戏时间自动选用）")]
    [Tooltip("与 ActivityTrigger 相同：成功后是否再 POST /time/advance")]
    public bool advanceTimeAfterSceneActivity = true;
    [Tooltip("留空：按时间选 sleep(21点~6点前) 或 rest；非空则固定。时间：21点前休息+1小时，21点后/凌晨睡到下一日6:30")]
    public string activityDormSleep = "";
    [Tooltip("留空：图书馆按时段选 study_library_morning/afternoon/evening；非空则固定")]
    public string activitySelfStudy = "";
    public string activityExercise = "exercise";
    [Tooltip("室友聊天/夜间活动，对应 v2.1：chat_roommate（18点~次日1点）")]
    public string activityNightGaming = "chat_roommate";
    [Tooltip("留空：实验室按时段选 research_morning/afternoon/evening；非空则固定（如 consult_teacher 仅 8~17 点）")]
    public string activityLab = "";
    [Tooltip("留空：second(8)→8–18 时 tour_campus，beauty(7)→8–17 时 help_tourist；非空则两场景均用该固定 id")]
    public string activityClubOrMisc = "";
    public string activityEatCanteen = "eat_canteen";

    [Header("场景 R 键活动（v2.1：图书馆社团 / 操场约会 / 教室组会）")]
    public string activityLibraryClub = "club_activity";
    public string activityPlaygroundDate = "date_boyfriend";
    [Tooltip("教室社工组会，须与 GET /activities 一致")]
    public string activityClassroomSocialMeeting = "social_meeting";
    [Tooltip("实验室 R：导师面谈（v2.1 consult_mentor，通常 13–17 时）；须与 GET /activities 一致")]
    public string activityLabConsultMentor = "consult_mentor";
    [Tooltip("ActivityPresentationUI 插图键；可留空则用 activity id")]
    public string artKeyLibraryClub = "club_activity";
    public string artKeyPlaygroundDate = "date_boyfriend";
    public string artKeyClassroomSocialMeeting = "social_meeting";
    public string artKeyLabConsultMentor = "";

    private bool _activityBusy;

    public bool IsActivityBusy => _activityBusy;

    void Start()
    {
        if(animator==null)
        {
            animator=GetComponent<Animator>();
        }
        animator.applyRootMotion=false;
        environmentRb.constraints=RigidbodyConstraints2D.FreezeRotation;
        environmentRb.drag=10f;
        environmentRb.angularDrag=10f;
        rb.constraints=RigidbodyConstraints2D.FreezeRotation;
        rb.drag=10f;
        rb.angularDrag=10f;
        float cameraHeight = 2f*Camera.main.orthographicSize;
        float cameraWidth = cameraHeight*Camera.main.aspect;
        cameraHalfWidth = cameraWidth/2f;
        cameraHalfHeight = cameraHeight/2f;
        tr=FindObjectOfType<scenetrans>();
        original = environmentRb.position;
        if (GetComponent<LateNightCurfewMonitor>() == null)
            gameObject.AddComponent<LateNightCurfewMonitor>();
        if (GetComponent<LibraryHoursMonitor>() == null)
            gameObject.AddComponent<LibraryHoursMonitor>();
        _behAnimator = behave != null ? behave.GetComponent<Animator>() : null;
        APIManager.EnsureExists();
    }

    /// <summary>将 behave 挂到活动结算 UI 槽位并播放与场景活动一致的 Animator 触发器。</summary>
    public bool TryAttachBehaveForPresentation(RectTransform slot, string triggerName,
        out Transform origParent, out int origSibling, out bool origActive)
    {
        origParent = null;
        origSibling = 0;
        origActive = false;
        if (behave == null || slot == null || string.IsNullOrEmpty(triggerName)) return false;
        if (_behAnimator == null) _behAnimator = behave.GetComponent<Animator>();
        if (_behAnimator == null || _behAnimator.runtimeAnimatorController == null) return false;

        origParent = behave.transform.parent;
        origSibling = behave.transform.GetSiblingIndex();
        origActive = behave.activeSelf;

        bool wokeChain = AnyAncestorInactive(behave);
        EnsureGameObjectHierarchyActive(behave);
        behave.transform.SetParent(slot, false);
        if (behave.transform is RectTransform ort)
        {
            ort.anchorMin = Vector2.zero;
            ort.anchorMax = Vector2.one;
            ort.offsetMin = ort.offsetMax = Vector2.zero;
            ort.localScale = Vector3.one;
            ort.localRotation = Quaternion.identity;
            ort.anchoredPosition = Vector2.zero;
        }
        behave.SetActive(true);

        CacheBehaveRaycastOff();

        if (!behave.activeInHierarchy) return false;
        if (wokeChain)
        {
            _behAnimator.Rebind();
            _behAnimator.Update(0f);
        }
        if (_behAnimator.isActiveAndEnabled && _behAnimator.layerCount > 0)
        {
            var st = _behAnimator.GetCurrentAnimatorStateInfo(0);
            if (!st.IsName("normal"))
            {
                _behAnimator.Play("normal", 0, 0f);
                _behAnimator.Update(0f);
            }
        }
        _behAnimator.SetTrigger(triggerName);
        return true;
    }

    public void DetachBehaveFromPresentation(Transform origParent, int origSibling, bool origActive)
    {
        if (behave == null || origParent == null) return;
        RestoreBehaveRaycast();
        behave.transform.SetParent(origParent, false);
        if (origSibling >= 0 && origSibling < origParent.childCount)
            behave.transform.SetSiblingIndex(origSibling);
        behave.SetActive(origActive);
    }

    private void CacheBehaveRaycastOff()
    {
        if (behave == null) return;
        var gs = behave.GetComponentsInChildren<Graphic>(true);
        _behaveRaycastGraphics = gs;
        if (gs == null || gs.Length == 0)
        {
            _behaveRaycastPrev = null;
            return;
        }
        _behaveRaycastPrev = new bool[gs.Length];
        for (int i = 0; i < gs.Length; i++)
        {
            _behaveRaycastPrev[i] = gs[i].raycastTarget;
            gs[i].raycastTarget = false;
        }
    }

    private void RestoreBehaveRaycast()
    {
        if (_behaveRaycastGraphics == null || _behaveRaycastPrev == null) return;
        int n = Mathf.Min(_behaveRaycastGraphics.Length, _behaveRaycastPrev.Length);
        for (int i = 0; i < n; i++)
        {
            if (_behaveRaycastGraphics[i] != null)
                _behaveRaycastGraphics[i].raycastTarget = _behaveRaycastPrev[i];
        }
        _behaveRaycastGraphics = null;
        _behaveRaycastPrev = null;
    }

    private static bool AnyAncestorInactive(GameObject leaf)
    {
        if (leaf == null) return false;
        for (Transform t = leaf.transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf) return true;
        }
        return false;
    }

    private static void EnsureGameObjectHierarchyActive(GameObject leaf)
    {
        if (leaf == null) return;
        Transform t = leaf.transform;
        var chain = new List<GameObject>();
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                chain.Add(t.gameObject);
            t = t.parent;
        }
        for (int i = chain.Count - 1; i >= 0; i--)
            chain[i].SetActive(true);
    }

    private IEnumerator WaitPresentationDone()
    {
        while (ActivityPresentationUI.IsOpen)
            yield return null;
    }

    /// <summary>v2.1 图书馆自习；<paramref name="courseIdOrEmpty"/> 为空则不传 course_id。</summary>
    public IEnumerator RunLibrarySelfStudyFromUi(string courseIdOrEmpty)
    {
        if (_activityBusy) yield break;
        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        string activityId = activitySelfStudy;
        if (string.IsNullOrEmpty(activityId))
        {
            TimeInfoV21 t = null;
            string e = null;
            yield return FetchTimeForActivity(x => t = x, err => e = err);
            if (!string.IsNullOrEmpty(e) || t == null)
            {
                Debug.LogWarning("[playercontrol] 自习: " + (e ?? "无法获取时间"));
                ActivityPresentationUI.Instance.ShowFailure("图书馆自习", e ?? "无法获取游戏时间");
                yield return WaitPresentationDone();
                _activityBusy = false;
                yield break;
            }
            activityId = ActivitySceneIdsV21.LibraryStudyFromServerTime(t);
        }

        if (string.IsNullOrEmpty(activityId))
        {
            ActivityPresentationUI.Instance.ShowFailure("图书馆自习", "当前时段无法解析自习活动。",
                "study_library_morning");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string cid = string.IsNullOrWhiteSpace(courseIdOrEmpty) ? null : courseIdOrEmpty;
        var snap = PlayerStatsSnapshot.Capture();
        PlayerManager.SuppressActivityHudToasts = true;
        ActivityResultV21 res = null;
        string actErr = null;
        var flow = ServerActivityFlow.RunV21(activityId, cid, (r, e) => { res = r; actErr = e; });
        while (flow.MoveNext())
            yield return flow.Current;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(actErr) || res == null)
        {
            Debug.LogWarning("[playercontrol] 图书馆自习: " + (actErr ?? "无响应"));
            ActivityPresentationUI.Instance.ShowFailure("图书馆自习", actErr ?? "自习请求失败");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        if (!res.success)
        {
            ActivityPresentationUI.Instance.ShowFailure(
                res.activity_name ?? "图书馆自习",
                string.IsNullOrEmpty(res.activity_name) ? "当前无法进行图书馆自习。" : res.activity_name,
                activityId);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string title = string.IsNullOrEmpty(res.activity_name) ? "图书馆自习" : res.activity_name;
        string body = ActivityPresentationFormatter.FormatV21(res, snap);
        // #region agent log
        if (res.new_time != null)
            DebugSessionNdjson.LibraryStudyCompleted("H4", res.new_time.hour, res.new_time.minute, -1f);
        // #endregion
        ActivityPresentationUI.Instance.ShowSuccess(activityId, "read", title, body, null, false);
        yield return WaitPresentationDone();
        // 避免 GET /player 短暂返回旧数据，覆盖 execute 里已应用的 new_state（精力看起来「回升」）。
        if (res.new_state == null)
            PlayerManager.Instance?.RefreshFromServer();
        _activityBusy = false;
    }

    /// <summary>v2.1：固定 activity_id（无 course_id、无时段解析），用于社团 / 约会等。</summary>
    public IEnumerator RunFixedActivityV21(string activityId, string userFacingLabel, string artKey)
    {
        if (_activityBusy) yield break;
        if (string.IsNullOrEmpty(activityId)) yield break;
        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        var snap = PlayerStatsSnapshot.Capture();
        PlayerManager.SuppressActivityHudToasts = true;
        ActivityResultV21 res = null;
        string actErr = null;
        var flow = ServerActivityFlow.RunV21(activityId, null, (r, e) => { res = r; actErr = e; });
        while (flow.MoveNext())
            yield return flow.Current;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(actErr) || res == null)
        {
            Debug.LogWarning("[playercontrol] " + userFacingLabel + ": " + (actErr ?? "无响应"));
            ActivityPresentationUI.Instance.ShowFailure(userFacingLabel, actErr ?? "请求失败");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        if (!res.success)
        {
            ActivityPresentationUI.Instance.ShowFailure(
                res.activity_name ?? userFacingLabel,
                string.IsNullOrEmpty(res.activity_name) ? "当前无法进行该活动。" : res.activity_name,
                activityId);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string title = string.IsNullOrEmpty(res.activity_name) ? userFacingLabel : res.activity_name;
        string body = ActivityPresentationFormatter.FormatV21(res, snap);
        string key = string.IsNullOrEmpty(artKey) ? activityId : artKey;
        ActivityPresentationUI.Instance.ShowSuccess(activityId, key, title, body, null, false);
        yield return WaitPresentationDone();
        if (res.new_state == null)
            PlayerManager.Instance?.RefreshFromServer();
        _activityBusy = false;
    }

    private IEnumerator RunKeyedActivity(string activityId, string artKey)
    {
        if (_activityBusy || string.IsNullOrEmpty(activityId)) yield break;
        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();
        var snap = PlayerStatsSnapshot.Capture();
        PlayerManager.SuppressActivityHudToasts = true;

        bool useV21Eat = activityId == activityEatCanteen;
        if (useV21Eat)
        {
            TimeInfoV21 timeBeforeEat = null;
            string timeErr = null;
            yield return FetchTimeForActivity(t => timeBeforeEat = t, e => timeErr = e);
            if (!string.IsNullOrEmpty(timeErr))
                Debug.LogWarning("[playercontrol] 食堂活动前获取时间失败（餐段登记可能不准确）: " + timeErr);

            ActivityResultV21 resV21 = null;
            string terrV21 = null;
            var flowV21 = ServerActivityFlow.RunV21(activityId, null,
                (r, e) => { resV21 = r; terrV21 = e; },
                string.IsNullOrEmpty(timeErr) && timeBeforeEat != null ? timeBeforeEat : null);
            while (flowV21.MoveNext())
                yield return flowV21.Current;
            PlayerManager.SuppressActivityHudToasts = false;

            if (!string.IsNullOrEmpty(terrV21) || resV21 == null)
            {
                Debug.LogWarning("[playercontrol] 活动请求失败 " + activityId + ": " + (terrV21 ?? "无响应"));
                ActivityPresentationUI.Instance.ShowFailure("活动失败", terrV21 ?? "无响应");
                yield return WaitPresentationDone();
                _activityBusy = false;
                yield break;
            }

            if (!resV21.success)
            {
                Debug.LogWarning("[playercontrol] 活动未成功: " + (resV21.activity_name ?? activityId));
                string msg = string.IsNullOrEmpty(resV21.activity_name) ? "当前无法进行该活动。" : resV21.activity_name;
                ActivityPresentationUI.Instance.ShowFailure(resV21.activity_name ?? "活动", msg, activityId);
                yield return WaitPresentationDone();
                _activityBusy = false;
                yield break;
            }

            string titleV21 = string.IsNullOrEmpty(resV21.activity_name) ? "活动完成" : resV21.activity_name;
            string bodyV21 = ActivityPresentationFormatter.FormatV21(resV21, snap);
            ActivityPresentationUI.Instance.ShowSuccess(activityId, artKey, titleV21, bodyV21, null, false);
            yield return WaitPresentationDone();
            if (resV21.new_state == null)
                PlayerManager.Instance?.RefreshFromServer();
            _activityBusy = false;
            yield break;
        }

        ActivityExecuteResult res = null;
        string terr = null;
        var flow = ServerActivityFlow.Run(activityId, advanceTimeAfterSceneActivity,
            (r, e) => { res = r; terr = e; });
        while (flow.MoveNext())
            yield return flow.Current;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(terr) || res == null)
        {
            Debug.LogWarning("[playercontrol] 活动请求失败 " + activityId + ": " + (terr ?? "无响应"));
            ActivityPresentationUI.Instance.ShowFailure("活动失败", terr ?? "无响应");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        if (!res.success)
        {
            Debug.LogWarning("[playercontrol] 活动未成功: " + (res.message ?? activityId));
            string msg = string.IsNullOrEmpty(res.message) ? "当前无法进行该活动。" : res.message;
            ActivityPresentationUI.Instance.ShowFailure(res.activity_name ?? "活动", msg, activityId);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string title = string.IsNullOrEmpty(res.activity_name) ? "活动完成" : res.activity_name;
        string body = ActivityPresentationFormatter.FormatLegacy(res, snap);
        ActivityPresentationUI.Instance.ShowSuccess(activityId, artKey, title, body, null, false);
        yield return WaitPresentationDone();
        _activityBusy = false;
    }

    private IEnumerator FetchTimeForActivity(Action<TimeInfoV21> onOk, Action<string> onErr)
    {
        bool done = false;
        TimeInfoV21 time = null;
        string terr = null;
        APIManager.Instance.GetTimeV21(t => { time = t; done = true; }, e => { terr = e; done = true; });
        while (!done) yield return null;
        if (!string.IsNullOrEmpty(terr) || time == null) onErr?.Invoke(terr ?? "无法获取时间");
        else onOk?.Invoke(time);
    }

    private IEnumerator RunResolvedSceneActivity(string inspectorOverride,
        Func<TimeInfoV21, string> resolveFromTime, string logLabel, string artKey)
    {
        if (_activityBusy) yield break;
        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        string id = inspectorOverride;
        if (string.IsNullOrEmpty(id))
        {
            TimeInfoV21 t = null;
            string e = null;
            yield return FetchTimeForActivity(x => t = x, err => e = err);
            if (!string.IsNullOrEmpty(e))
            {
                Debug.LogWarning("[playercontrol] " + logLabel + e);
                ActivityPresentationUI.Instance.ShowFailure("活动", logLabel + e);
                yield return WaitPresentationDone();
                _activityBusy = false;
                yield break;
            }
            id = resolveFromTime(t);
        }

        if (string.IsNullOrEmpty(id))
        {
            string hintForEmpty = null;
            if (logLabel != null)
            {
                if (logLabel.IndexOf("游览校园", StringComparison.Ordinal) >= 0)
                    hintForEmpty = "tour_campus";
                else if (logLabel.IndexOf("帮助游客", StringComparison.Ordinal) >= 0)
                    hintForEmpty = "help_tourist";
                else if (logLabel.IndexOf("实验室", StringComparison.Ordinal) >= 0)
                    hintForEmpty = "research_morning";
            }

            ActivityPresentationUI.Instance.ShowFailure("活动", logLabel + "当前无法进行（时间或条件不足）。",
                hintForEmpty);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        var snap = PlayerStatsSnapshot.Capture();
        PlayerManager.SuppressActivityHudToasts = true;
        ActivityExecuteResult res = null;
        string terr = null;
        var flow = ServerActivityFlow.Run(id, advanceTimeAfterSceneActivity,
            (r, e) => { res = r; terr = e; });
        while (flow.MoveNext())
            yield return flow.Current;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(terr) || res == null)
        {
            Debug.LogWarning("[playercontrol] 活动请求失败 " + id + ": " + (terr ?? "无响应"));
            ActivityPresentationUI.Instance.ShowFailure("活动失败", terr ?? "无响应");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        if (!res.success)
        {
            string msg = string.IsNullOrEmpty(res.message) ? "当前无法进行该活动。" : res.message;
            ActivityPresentationUI.Instance.ShowFailure(res.activity_name ?? "活动", msg, id);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string title = string.IsNullOrEmpty(res.activity_name) ? "活动完成" : res.activity_name;
        string body = ActivityPresentationFormatter.FormatLegacy(res, snap);
        ActivityPresentationUI.Instance.ShowSuccess(id, artKey, title, body, null, false);
        yield return WaitPresentationDone();
        _activityBusy = false;
    }

    /// <summary>00:50～6:30 未回宿舍：纯前端——与地图选宿舍相同传送至 scene 9，本地扣除精力/健康，睡觉同款 UI（不依赖后端推进或 PATCH）。</summary>
    /// <param name="debounceGameDayBlock"><see cref="LateNightCurfewMonitor"/> 用的游戏日块下标；仅在传送成功后写入防抖。</param>
    public void RequestForcedDormSleepFromCurfew(int debounceGameDayBlock = int.MinValue)
    {
        StartCoroutine(ForcedDormSleepFromCurfewRoutine(debounceGameDayBlock));
    }

    /// <summary>与 <see cref="panelctrl.OnButtonClick"/> 选宿舍索引一致：关闭大地图、<c>tr.scene = 9</c>，下一帧 <c>Update</c> 应用宿舍背景与坐标。</summary>
    private void TeleportToDormitoryLikeMapPanel()
    {
        var mapPanel = FindObjectOfType<panelctrl>();
        mapPanel?.CloseMapIfOpenForTeleport();
        if (tr == null)
            tr = FindObjectOfType<scenetrans>();
        if (tr == null) return;
        tr.scene = 9;
        lastscene = -1;
    }

    /// <summary>与地图选馆外一致：关闭大地图、<c>tr.scene = 6</c>。</summary>
    private void TeleportToLibraryExteriorLikeMapPanel()
    {
        var mapPanel = FindObjectOfType<panelctrl>();
        mapPanel?.CloseMapIfOpenForTeleport();
        if (tr == null)
            tr = FindObjectOfType<scenetrans>();
        if (tr == null) return;
        tr.scene = 6;
        lastscene = -1;
    }

    /// <summary>闭馆时在馆内强制至馆外；<paramref name="debounceGameDayBlock"/> 供 <see cref="LibraryHoursMonitor"/> 防抖。</summary>
    public void RequestEjectFromLibraryWhenClosed(int debounceGameDayBlock = int.MinValue)
    {
        StartCoroutine(EjectFromLibraryWhenClosedRoutine(debounceGameDayBlock));
    }

    private IEnumerator EjectFromLibraryWhenClosedRoutine(int debounceGameDayBlock)
    {
        CloseNpcOverlaysForCurfew();

        if (_activityBusy || ActivityPresentationUI.IsOpen)
            yield break;
        if (tr == null)
            tr = FindObjectOfType<scenetrans>();
        if (tr == null)
        {
            Debug.LogWarning("[playercontrol] 图书馆闭馆传送失败：未找到 scenetrans");
            yield break;
        }

        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        TeleportToLibraryExteriorLikeMapPanel();
        yield return null;

        var libMon = GetComponent<LibraryHoursMonitor>();
        if (libMon != null && debounceGameDayBlock != int.MinValue)
            libMon.NotifyLibraryHoursTeleportCommitted(debounceGameDayBlock);

        ActivityPresentationUI.Instance.ShowFailure(
            "图书馆闭馆", "闭馆时间已到，你已被传送至图书馆外。", ActivityUnlockHints.LibraryClosedContext);
        yield return WaitPresentationDone();

        _activityBusy = false;
    }

    /// <summary>宵禁强制回宿舍前关闭 NPC AI 聊天与静态 DialogueBox，避免 <c>canmove=false</c> / 聊天层遮挡导致流程像「未执行」。</summary>
    private static void CloseNpcOverlaysForCurfew()
    {
        bool closedChat = false, closedDlg = false;
        if (NPCManager.Instance != null && NPCManager.Instance.IsChatOpen)
        {
            NPCManager.Instance.HideChatUI();
            closedChat = true;
        }

        if (DialogueBox.Instance != null)
        {
            DialogueBox.Instance.Hide();
            closedDlg = true;
        }

        // #region agent log
        if (closedChat || closedDlg)
            DebugSessionNdjson.CurfewClosedNpcOverlays(closedChat, closedDlg);
        // #endregion
    }

    private IEnumerator ForcedDormSleepFromCurfewRoutine(int debounceGameDayBlock)
    {
        CloseNpcOverlaysForCurfew();

        if (_activityBusy || ActivityPresentationUI.IsOpen)
            yield break;
        if (tr == null)
            tr = FindObjectOfType<scenetrans>();
        if (tr == null)
        {
            Debug.LogWarning("[playercontrol] 宵禁失败：未找到 scenetrans");
            yield break;
        }

        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        TeleportToDormitoryLikeMapPanel();
        yield return null;

        var curfewMon = GetComponent<LateNightCurfewMonitor>();
        if (curfewMon != null && debounceGameDayBlock != int.MinValue)
            curfewMon.NotifyCurfewTeleportCommitted(debounceGameDayBlock);

        // #region agent log
        DebugSessionNdjson.CurfewUiForced(
            "H3", "show_晚归休息_ui", tr != null ? tr.scene : -1, debounceGameDayBlock);
        // #endregion

        APIManager.EnsureExists();
        if (APIManager.Instance == null)
        {
            Debug.LogWarning("[playercontrol] 宵禁失败：APIManager 缺失");
            ActivityPresentationUI.Instance.ShowFailure("晚归休息", "无法连接后端");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        var snap = PlayerStatsSnapshot.Capture();
        ActivityResultV21 res = null;
        string actErr = null;
        bool actDone = false;
        PlayerManager.SuppressActivityHudToasts = true;
        APIManager.Instance.ExecuteActivityV21WithFlags("sleep", new[] { "curfew_penalty" },
            r => { res = r; actDone = true; },
            e => { actErr = e; actDone = true; });
        while (!actDone) yield return null;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(actErr) || res == null)
        {
            Debug.LogWarning("[playercontrol] 晚归活动失败: " + (actErr ?? "无响应"));
            ActivityPresentationUI.Instance.ShowFailure("晚归休息", actErr ?? "请求失败");
            yield return WaitPresentationDone();
            bool rf = false;
            PlayerManager.Instance?.RefreshFromServer(() => rf = true);
            if (PlayerManager.Instance != null)
                while (!rf) yield return null;
            _activityBusy = false;
            yield break;
        }

        if (!res.success)
        {
            string msg = string.IsNullOrEmpty(res.activity_name) ? "晚归结算未完成。" : res.activity_name;
            ActivityPresentationUI.Instance.ShowFailure("晚归休息", msg, ActivityUnlockHints.SleepCurfewContext);
            yield return WaitPresentationDone();
            bool rf = false;
            PlayerManager.Instance?.RefreshFromServer(() => rf = true);
            if (PlayerManager.Instance != null)
                while (!rf) yield return null;
            _activityBusy = false;
            yield break;
        }

        PlayerManager.Instance?.ProcessActivityResultV21(res, "sleep", null);

        string body = "因晚归，你已被送回宿舍休息（与地图传送相同）。精力与健康因熬夜下降。";
        if (res.new_state != null)
        {
            int e0 = snap.energy;
            int h0 = snap.health;
            int e2 = res.new_state.energy;
            int h2 = res.new_state.health;
            body += "\n晚归：精力　" + e0 + " → " + e2 + "　(" + (e2 - e0) + ")";
            body += "\n晚归：健康　" + h0 + " → " + h2 + "　(" + (h2 - h0) + ")";
        }

        ActivityPresentationUI.Instance.ShowSuccess("sleep", "sleep", "晚归休息", body, "sleep");
        yield return WaitPresentationDone();

        bool refreshed = false;
        PlayerManager.Instance?.RefreshFromServer(() => refreshed = true);
        if (PlayerManager.Instance != null)
            while (!refreshed) yield return null;

        DailySummaryUI.EnsureExists();
        DailySummaryUI.Instance?.OpenAfterOvernightSleep();

        _activityBusy = false;
    }

    private IEnumerator RunDormActivityInternal()
    {
        if (_activityBusy) yield break;
        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        TimeInfoV21 timeBefore = null;
        string timeErr = null;
        yield return FetchTimeForActivity(t => timeBefore = t, e => timeErr = e);
        if (!string.IsNullOrEmpty(timeErr) || timeBefore == null)
        {
            Debug.LogWarning("[playercontrol] 宿舍活动: " + (timeErr ?? "无法获取时间"));
            ActivityPresentationUI.Instance.ShowFailure("宿舍活动", timeErr ?? "无法获取游戏时间");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string activityId = string.IsNullOrEmpty(activityDormSleep)
            ? ActivitySceneIdsV21.DormSleepOrRestFromServerTime(timeBefore)
            : activityDormSleep;
        if (string.IsNullOrEmpty(activityId))
        {
            ActivityPresentationUI.Instance.ShowFailure("宿舍活动", "当前无法解析休息/睡觉活动。");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        var snap = PlayerStatsSnapshot.Capture();
        PlayerManager.SuppressActivityHudToasts = true;
        ActivityResultV21 res = null;
        string actErr = null;
        var flow = ServerActivityFlow.RunV21(activityId, null, (r, e) => { res = r; actErr = e; });
        while (flow.MoveNext())
            yield return flow.Current;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(actErr) || res == null)
        {
            Debug.LogWarning("[playercontrol] 宿舍活动失败: " + (actErr ?? "无响应"));
            ActivityPresentationUI.Instance.ShowFailure("宿舍活动", actErr ?? "请求失败");
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        if (!res.success)
        {
            string msg = string.IsNullOrEmpty(res.activity_name) ? "宿舍活动未完成。" : res.activity_name;
            ActivityPresentationUI.Instance.ShowFailure("宿舍活动", msg, activityId);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        TimeInfoV21 timeAfter = null;
        timeErr = null;
        yield return FetchTimeForActivity(t => timeAfter = t, e => timeErr = e);
        if (!string.IsNullOrEmpty(timeErr) || timeAfter == null)
            Debug.LogWarning("[playercontrol] 宿舍活动后无法获取时间，跳过对齐: " + (timeErr ?? "空"));

        int correction = ActivitySceneIdsV21.DormActivityCorrectionMinutes(timeBefore, timeAfter);
        if (correction > 0 && APIManager.Instance != null)
        {
            bool advDone = false;
            APIManager.Instance.AdvanceTimeMinutesV21(correction, () => advDone = true,
                e => { Debug.LogWarning("[playercontrol] 宿舍对齐时间失败: " + e); advDone = true; });
            while (!advDone) yield return null;
        }

        string title = string.IsNullOrEmpty(res.activity_name) ? "宿舍活动" : res.activity_name;
        string body = ActivityPresentationFormatter.FormatV21(res, snap);

        ActivityPresentationUI.Instance.ShowSuccess(activityId, activityId, title, body, "sleep");
        yield return WaitPresentationDone();

        bool refreshed = false;
        var pmEnd = PlayerManager.Instance;
        if (pmEnd != null)
            pmEnd.RefreshFromServer(() => refreshed = true);
        else
            refreshed = true;
        while (!refreshed) yield return null;

        // 通宵睡觉已由活动把时钟推到次日 6:30，不会在宵禁窗口内停留，DayEndSummaryMonitor 无法触发；在此补开每日总结。
        if (string.Equals(activityId, "sleep", StringComparison.Ordinal))
        {
            DailySummaryUI.EnsureExists();
            DailySummaryUI.Instance?.OpenAfterOvernightSleep();
        }

        _activityBusy = false;
    }

    private IEnumerator RunLabActivity() =>
        RunResolvedSceneActivity(activityLab,
            ActivitySceneIdsV21.ResearchLabFromServerTime, "实验室: ", "lab");

    private IEnumerator RunTourCampusSceneActivity() =>
        RunResolvedSceneActivity(activityClubOrMisc,
            ActivitySceneIdsV21.TourCampusFromServerTime, "游览校园: ", "pic1");

    private IEnumerator RunHelpTouristSceneActivity() =>
        RunResolvedSceneActivity(activityClubOrMisc,
            ActivitySceneIdsV21.HelpTouristFromServerTime, "帮助游客: ", "pic2");

    private IEnumerator RunAttendClassScene()
    {
        if (_activityBusy) yield break;
        _activityBusy = true;
        ActivityPresentationUI.EnsureExists();

        PlayerManager.SuppressActivityHudToasts = true;
        List<AttendClassResponseV21> batch = null;
        string terr = null;
        var flow = ServerAttendClassFlow.RunAllCoursesForCurrentPeriod(advanceTimeAfterSceneActivity,
            (list, e) => { batch = list; terr = e; });
        while (flow.MoveNext())
            yield return flow.Current;
        PlayerManager.SuppressActivityHudToasts = false;

        if (!string.IsNullOrEmpty(terr))
        {
            Debug.LogWarning("[playercontrol] 上课请求失败: " + terr);
            ActivityPresentationUI.Instance.ShowFailure("上课", terr);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        if (batch == null || batch.Count == 0)
        {
            Debug.LogWarning("[playercontrol] 上课：无有效响应");
            ActivityPresentationUI.Instance.ShowFailure("上课", "本节上课未成功（请看时间与课表）。",
                ActivityUnlockHints.AttendClassContext);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        bool anyOk = false;
        foreach (var r in batch)
        {
            if (r != null && r.success) anyOk = true;
        }

        if (!anyOk)
        {
            Debug.LogWarning("[playercontrol] 上课未成功（全部课程未通过）");
            ActivityPresentationUI.Instance.ShowFailure("上课", "本节上课未成功（全班课程均未通过）。",
                ActivityUnlockHints.AttendClassContext);
            yield return WaitPresentationDone();
            _activityBusy = false;
            yield break;
        }

        string body = ActivityPresentationFormatter.FormatAttendBatch(batch);
        ActivityPresentationUI.Instance.ShowSuccess("attend_class", "class", "上课", body, null, false);
        yield return WaitPresentationDone();
        _activityBusy = false;
    }

    void Update()
    {
        if(tr.scene==lastscene)
        {
            float moveX = 0f;
            float moveY = 0f;
            if(canmove)
            {
                if(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.D))
                {
                    if(Input.GetKey(KeyCode.A)) moveX=-1f;
                    if(Input.GetKey(KeyCode.D)) moveX=1f;
                }
                else if(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.S))
                {
                    if(Input.GetKey(KeyCode.W)) moveY=1f;
                    if(Input.GetKey(KeyCode.S)) moveY=-1f;
                }
            }
            movement=new Vector2(-moveX,-moveY).normalized;
            if(moveX==1f)
            {
                animator.Play("rmove");
                state=4;
            }
            else if(moveX==-1f)
            {
                animator.Play("lmove");
                state=2;
            }
            else if(moveY==1f)
            {
                animator.Play("bmove");
                state=1;
            }
            else if(moveY==-1f)
            {
                animator.Play("zmove");
                state=3;
            }
            else
            {
                switch(state)
                {
                    case 1:
                        animator.Play("bstand");
                        break;
                    case 2:
                        animator.Play("lstand");
                        break;
                    case 3:
                        animator.Play("zstand");
                        break;
                    case 4:
                        animator.Play("rstand");
                        break;
                }
                if(canmove&&tr.scene==9&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunDormActivityInternal());
                }
                else if(canmove&&tr.scene==11&&Input.GetKeyDown(KeyCode.E)
                        && !NPCManager.ShouldSuppressGlobalHotkeys())
                {
                    LibrarySelfStudyUI.OpenForPlayer(this);
                }
                else if(canmove&&tr.scene==11&&Input.GetKeyDown(KeyCode.R)
                        && !LibrarySelfStudyUI.IsOpen
                        && !NPCManager.ShouldSuppressGlobalHotkeys()
                        && !_activityBusy
                        && !ActivityPresentationUI.IsOpen
                        && !SceneRActivityUI.IsOpen)
                {
                    if (LibraryHoursV21.TryIsLibraryClosedFromPlayerCache(out bool libClosedR) && libClosedR)
                    {
                        ActivityPresentationUI.EnsureExists();
                        ActivityPresentationUI.Instance.ShowFailure("图书馆", "闭馆中，无法进行社团活动。",
                            ActivityUnlockHints.LibraryClubClosedContext);
                    }
                    else
                    {
                        SceneRActivityUI.OpenForSceneActivity(
                            this, activityLibraryClub, "社团活动",
                            string.IsNullOrEmpty(artKeyLibraryClub) ? activityLibraryClub : artKeyLibraryClub);
                    }
                }
                else if(canmove&&tr.scene==2&&Input.GetKeyDown(KeyCode.R)
                        && !NPCManager.ShouldSuppressGlobalHotkeys()
                        && !_activityBusy
                        && !ActivityPresentationUI.IsOpen
                        && !SceneRActivityUI.IsOpen)
                {
                    SceneRActivityUI.OpenForSceneActivity(
                        this, activityPlaygroundDate, "约会",
                        string.IsNullOrEmpty(artKeyPlaygroundDate) ? activityPlaygroundDate : artKeyPlaygroundDate);
                }
                else if(canmove&&tr.scene==2&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunKeyedActivity(activityExercise, "exercise"));
                }
                else if(canmove&&tr.scene==12&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunAttendClassScene());
                }
                else if(canmove&&tr.scene==12&&Input.GetKeyDown(KeyCode.R)
                        && !NPCManager.ShouldSuppressGlobalHotkeys()
                        && !LibrarySelfStudyUI.IsOpen
                        && !_activityBusy
                        && !ActivityPresentationUI.IsOpen
                        && !SceneRActivityUI.IsOpen)
                {
                    SceneRActivityUI.OpenForSceneActivity(
                        this, activityClassroomSocialMeeting, "组会",
                        string.IsNullOrEmpty(artKeyClassroomSocialMeeting) ? activityClassroomSocialMeeting : artKeyClassroomSocialMeeting);
                }
                else if(canmove&&tr.scene==9&&Input.GetKeyDown(KeyCode.R))
                {
                    StartCoroutine(RunKeyedActivity(activityNightGaming, "chat_roommate"));
                }
                else if(canmove&&tr.scene==10&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunLabActivity());
                }
                else if(canmove&&tr.scene==10&&Input.GetKeyDown(KeyCode.R)
                        && !NPCManager.ShouldSuppressGlobalHotkeys()
                        && !_activityBusy
                        && !ActivityPresentationUI.IsOpen
                        && !SceneRActivityUI.IsOpen)
                {
                    SceneRActivityUI.OpenForSceneActivity(
                        this, activityLabConsultMentor, "导师面谈",
                        string.IsNullOrEmpty(artKeyLabConsultMentor) ? activityLabConsultMentor : artKeyLabConsultMentor);
                }
                else if(canmove&&tr.scene==8&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunTourCampusSceneActivity());
                }
                else if(canmove&&tr.scene==7&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunHelpTouristSceneActivity());
                }
                else if(canmove&&tr.scene==13&&Input.GetKeyDown(KeyCode.E))
                {
                    StartCoroutine(RunKeyedActivity(activityEatCanteen, "eat_canteen"));
                }
            }
        }
        else
{
    switch(tr.scene)
    {
        case 0:
            rb.MovePosition(new Vector2(0,0));
            if(lastscene==1)
            {
                rb.position=new Vector2(0f,3.6f);
                environmentRb.position=new Vector2(1.67f,-11.92f);
            }
            else if(lastscene==2)
            {
                rb.position=new Vector2(7.8f,-0.18f);
                environmentRb.position=new Vector2(0.17f,-7.62f);
            }
            else if(lastscene==3)
            {
                rb.position=new Vector2(0f,-3.6f);
                environmentRb.position=new Vector2(1.67f,14.88f);
            }
            else if(lastscene==4)
            {
                rb.position=new Vector2(7.8f,-0.48f);
                environmentRb.position=new Vector2(0.17f,14.88f);
            }
            else if(lastscene==5)
            {
                rb.position=new Vector2(7.8f,2.02f);
                environmentRb.position=new Vector2(0.17f,-11.92f);
            }
            else if(lastscene==6)
            {
                rb.position=new Vector2(-7.8f,2.02f);
                environmentRb.position=new Vector2(2.47f,-7.62f);
            }
            else if(lastscene==7)
            {
                rb.position=new Vector2(-7.8f,-0.48f);
                environmentRb.position=new Vector2(2.47f,-14.88f);
            }
            else if(lastscene==8)
            {
                rb.position=new Vector2(-7.8f,-0.18f);
                environmentRb.position=new Vector2(2.47f,-7.62f);
            }
            lastscene=0;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 1:
            rb.MovePosition(new Vector2(0f,-1f));
            environmentRb.position=original;
            if(lastscene==13)
            {
                rb.position=new Vector2(0f,0f);
                environmentRb.position=new Vector2(1.27f,-3.12f);
            }
            lastscene=1;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 2:
            environmentRb.position=original;
            lastscene=2;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            rb.MovePosition(new Vector2(-8.8f,-4f));
            break;
        case 3:
            rb.MovePosition(new Vector2(0f,4f));
            environmentRb.position=original;
            if(lastscene==12)
            {
                rb.position=new Vector2(0f,0f);
                environmentRb.position=new Vector2(1.27f,-0.72f);
            }
            lastscene=3;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 4:
            rb.MovePosition(new Vector2(-8f,0f));
            environmentRb.position=original;
            if(lastscene==10)
            {
                rb.position=new Vector2(0f,0f);
                environmentRb.position=new Vector2(-11.98f,3.16f);
            }
            lastscene=4;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 5:
            rb.MovePosition(new Vector2(0f,-2f));
            environmentRb.position=original;
            if(lastscene==9)
            {
                rb.position=new Vector2(0f,0f);
                environmentRb.position=new Vector2(1.27f,-1.22f);
            }
            lastscene=5;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 6:
            rb.MovePosition(new Vector2(8f,-0.91f));
            environmentRb.position=original;
            if(lastscene==11)
            {
                rb.position=new Vector2(0f,0f);
                environmentRb.position=new Vector2(19.26f,-6.01f);
            }
            lastscene=6;
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 7:
            environmentRb.position=original;
            lastscene=7;
            rb.MovePosition(new Vector2(8f,0f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 8:
            environmentRb.position=original;
            lastscene=8;
            rb.MovePosition(new Vector2(0f,-3.6f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 9:
            environmentRb.position=original;
            lastscene=9;
            rb.MovePosition(new Vector2(0f,-1.5f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 10:
            environmentRb.position=original;
            lastscene=10;
            rb.MovePosition(new Vector2(0f,0f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 11:
            environmentRb.position=original;
            lastscene=11;
            rb.MovePosition(new Vector2(0f,-4f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 12:
            environmentRb.position=original;
            lastscene=12;
            rb.MovePosition(new Vector2(0f,0f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
        case 13:
            environmentRb.position=original;
            lastscene=13;
            rb.MovePosition(new Vector2(0f,-1.6f));
            for(int i = 0;i<14;i++)
            {
                if(i==tr.scene) continue;
                back[i].SetActive(false);
            }
            back[tr.scene].SetActive(true);
            break;
    }
}
        SpriteRenderer sprite = back[tr.scene].GetComponent<SpriteRenderer>();
        Bounds bounds = sprite.bounds;
        xmin=bounds.min.x;
        xmax=bounds.max.x;
        ymin=bounds.min.y;
        ymax=bounds.max.y;
        l=(-cameraHalfWidth)>xmin;
        r=(cameraHalfWidth)<xmax;
        d=(-cameraHalfHeight)>ymin;
        u=(cameraHalfHeight)<ymax;
    }

    void FixedUpdate()
    {
        if(movement==new Vector2(0f,1f)&&!u&&rb.position.y<0)
        {
            Vector2 newPosition = environmentRb.position+movement*moveSpeed*Time.fixedDeltaTime;
            environmentRb.MovePosition(newPosition);
        }
        else if(movement==new Vector2(0f,-1f)&&!d&&rb.position.y>0)
        {
            Vector2 newPosition = environmentRb.position+movement*moveSpeed*Time.fixedDeltaTime;
            environmentRb.MovePosition(newPosition);
        }
        if(movement==new Vector2(1f,0f)&&!r&&rb.position.x<0)
        {
            Vector2 newPosition = environmentRb.position+movement*moveSpeed*Time.fixedDeltaTime;
            environmentRb.MovePosition(newPosition);
        }
        else if(movement==new Vector2(-1f,0f)&&!l&&rb.position.x>0)
        {
            Vector2 newPosition = environmentRb.position+movement*moveSpeed*Time.fixedDeltaTime;
            environmentRb.MovePosition(newPosition);
        }
        if((!(l&&r)&&(movement==new Vector2(1f,0f)||movement==new Vector2(-1f,0f)))
            ||(!(d&&u)&&(movement==new Vector2(0f,1f)||movement==new Vector2(0f,-1f))))
        {
            environmentRb.bodyType=RigidbodyType2D.Kinematic;
            rb.bodyType=RigidbodyType2D.Dynamic;
            Vector2 newPosition = rb.position-movement*moveSpeed*Time.fixedDeltaTime;
            rb.MovePosition(newPosition);
        }
        else if(movement!=Vector2.zero)
        {
            rb.bodyType=RigidbodyType2D.Kinematic;
            environmentRb.bodyType=RigidbodyType2D.Dynamic;
            Vector2 newPosition = environmentRb.position+movement*moveSpeed*Time.fixedDeltaTime;
            environmentRb.MovePosition(newPosition);
        }
        else
        {
            rb.bodyType=RigidbodyType2D.Kinematic;
            environmentRb.bodyType=RigidbodyType2D.Kinematic;
        }
    }
}