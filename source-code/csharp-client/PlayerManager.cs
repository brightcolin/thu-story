using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using QinghuaStory;

/// <summary>
/// 玩家状态本地缓存，与服务端 GET /player 同步。
/// 统一经 APIManager 通信。
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    /// <summary>为 true 时，活动/上课结算不弹出 HUD 横幅（由 ActivityPresentationUI 统一展示）。</summary>
    public static bool SuppressActivityHudToasts { get; set; }

    [Header("设置")]
    public bool fetchOnStart = true;

    /// <summary>为 true 时，好友度以服务端为准，FriendshipPersistence 不会在启动时用本地覆盖内存。</summary>
    public bool serverFriendshipsAuthoritative = true;

    [Header("当前玩家数据（Inspector 查看）")]
    public PlayerStatsData stats = new();

    public PlayerStatsData Stats => stats;

    public int CurrentWeek => stats.current_week;
    public string CurrentPhase => stats.current_phase;

    private readonly Dictionary<string, int> _friendships = new();

    public static event Action<string, int> FriendshipChanged;
    /// <summary>首次拉取后不触发；之后 semester_index 变化时调用。</summary>
    public static event Action<int, int> SemesterIndexChanged;

    private int _lastSemesterIndex = int.MinValue;
    private bool _semesterTracked;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // DontDestroyOnLoad 仅对根物体有效；玩家在场景子层级时需挂到持久化根下
        if (transform.parent != null)
        {
            var holder = new GameObject("Persistent_PlayerSystems");
            DontDestroyOnLoad(holder);
            transform.SetParent(holder.transform, true);
        }
        else
            DontDestroyOnLoad(gameObject);

        APIManager.EnsureExists();

        if (GetComponent<FriendshipPersistence>() == null && FindObjectOfType<FriendshipPersistence>() == null)
            gameObject.AddComponent<FriendshipPersistence>();

        if (FindObjectOfType<SemesterTranscriptUI>(true) == null)
        {
            var trGo = new GameObject("SemesterTranscriptUI");
            trGo.AddComponent<SemesterTranscriptUI>();
            trGo.transform.SetParent(transform.parent != null ? transform.parent : transform, false);
        }

        MealReminderUiGate.SetRunner(this);
        MealReminderUiGate.InstallPresentationClosedHook();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            MealReminderUiGate.UninstallPresentationClosedHook();
    }

    private void Start()
    {
        APIManager.EnsureExists();
        if (APIManager.Instance == null)
            Debug.LogError("[PlayerManager] APIManager 未就绪。");

        if (fetchOnStart) StartCoroutine(FetchPlayerStateRoutine());
    }

    // ══════════════════════════════════════════
    // 外部接口
    // ══════════════════════════════════════════

    public void OnNpcFriendshipChanged(string npcId, int newValue)
    {
        _friendships[npcId] = newValue;
        GameHUD.Instance?.RefreshFriendship(npcId, newValue);
        FriendshipChanged?.Invoke(npcId, newValue);
    }

    public void LoadFriendships(IReadOnlyDictionary<string, int> data)
    {
        if (data == null) return;
        foreach (var kv in data)
        {
            _friendships[kv.Key] = Mathf.Clamp(kv.Value, 0, 100);
            GameHUD.Instance?.RefreshFriendship(kv.Key, _friendships[kv.Key]);
        }
    }

    public FriendshipsData GetAllFriendshipsForSave()
    {
        var data = new FriendshipsData();
        foreach (var kv in _friendships)
            data.entries.Add(new FriendshipEntry { npcId = kv.Key, value = kv.Value });
        return data;
    }

    public int GetFriendship(string npcId)
    {
        _friendships.TryGetValue(npcId, out int v);
        return v;
    }

    /// <summary>好感度字典副本（用于日结基线等，勿长期缓存）。</summary>
    public Dictionary<string, int> GetFriendshipsSnapshot() => new Dictionary<string, int>(_friendships);

    public void ClearFriendships()
    {
        _friendships.Clear();
        GameHUD.Instance?.ClearFriendshipsDisplay();
    }

    /// <summary>存档重置（POST /save/reset）并重新拉取 /player</summary>
    public void RequestResetFriendshipsOnServer(MonoBehaviour runner)
    {
        if (runner != null) runner.StartCoroutine(ResetGameOnServerThenRefreshRoutine());
    }

    private IEnumerator ResetGameOnServerThenRefreshRoutine()
    {
        if (APIManager.Instance == null)
        {
            Debug.LogWarning("[PlayerManager] APIManager 缺失，跳过重置请求");
            yield break;
        }

        bool done = false;
        APIManager.Instance.ResetGame(
            _ => done = true,
            err => { Debug.LogWarning("[PlayerManager] 重置失败: " + err); done = true; });
        while (!done) yield return null;

        yield return FetchPlayerStateRoutine();
    }

    public void ApplyStatDelta(float gpa = 0, int energy = 0, int health = 0,
        int math = 0, int eng = 0, int res = 0, int soc = 0, int wis = 0)
    {
        stats.gpa = Mathf.Clamp(stats.gpa + gpa, 0f, 4f);
        stats.energy = Mathf.Clamp(stats.energy + energy, 0, 100);
        stats.health = Mathf.Clamp(stats.health + health, 0, 100);
        stats.math_skill = Mathf.Clamp(stats.math_skill + math, 1, 10);
        stats.english_skill = Mathf.Clamp(stats.english_skill + eng, 1, 10);
        stats.research_skill = Mathf.Clamp(stats.research_skill + res, 1, 10);
        stats.social_skill = Mathf.Clamp(stats.social_skill + soc, 1, 10);
        stats.research_ability_100 = Mathf.Clamp(stats.research_ability_100 + res * 10, 0, 100);
        stats.social_ability_100 = Mathf.Clamp(stats.social_ability_100 + soc * 10, 0, 100);
        stats.wisdom = Mathf.Clamp(stats.wisdom + wis, 0, 999);

        GameHUD.Instance?.RefreshStats(stats);
        CheckWarnings();
    }

    /// <summary>从服务端推进时段（POST /time/advance），成功后刷新 /player。</summary>
    public void AdvancePhase(Action onComplete = null)
    {
        StartCoroutine(AdvancePhaseRoutine(onComplete));
    }

    private IEnumerator AdvancePhaseRoutine(Action onComplete)
    {
        if (APIManager.Instance == null)
        {
            Debug.LogWarning("[PlayerManager] 无法推进时间：APIManager 缺失");
            onComplete?.Invoke();
            yield break;
        }

        bool done = false;
        string terr = null;
        APIManager.Instance.AdvanceTime(
            _ => done = true,
            err => { terr = err; done = true; });
        while (!done) yield return null;

        if (!string.IsNullOrEmpty(terr))
        {
            Debug.LogWarning("[PlayerManager] 推进时间失败: " + terr);
            onComplete?.Invoke();
            yield break;
        }

        ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
        yield return FetchPlayerStateRoutine(onComplete);
    }

    /// <summary>立即拉取 GET /player 并更新本地显示。</summary>
    public void RefreshFromServer(Action onDone = null)
    {
        StartCoroutine(FetchPlayerStateRoutine(onDone));
    }

    /// <summary>由 GET /time（如 GameTimeHUD）写入，供宵禁在无单独 /time 失败时仍可按时刻判断。</summary>
    public void UpdateClientTimeCacheFromTimeApi(QinghuaStory.TimeInfoV21 t)
    {
        if (t == null) return;
        float prevTotal = stats.client_cached_total_game_minutes;
        stats.client_cached_game_hour = t.hour;
        stats.client_cached_game_minute = t.minute;
        stats.client_cached_total_game_minutes = t.total_game_minutes;
        if (!string.IsNullOrEmpty(t.phase))
            stats.current_phase = t.phase;
        if (t.phase_name != null)
            stats.server_phase_name = t.phase_name;
        MealDeadlineCrossingDetector.NotifyTimeAdvanced(prevTotal, stats.client_cached_total_game_minutes);
    }

    /// <summary>将服务端玩家负载写入 stats 与好感度（供活动结果等复用）。</summary>
    public void ApplyPlayerPayload(PlayerStatePayload p)
    {
        if (p == null) return;
        stats.gpa = Mathf.Clamp(p.gpa, 0f, 4f);
        stats.energy = Mathf.Clamp(p.energy, 0, 100);
        stats.health = Mathf.Clamp(p.health, 0, 100);
        stats.wisdom = Mathf.Max(0, p.wisdom);
        stats.math_skill = Mathf.Clamp(p.math_skill, 1, 10);
        stats.english_skill = Mathf.Clamp(p.english_skill, 1, 10);
        stats.research_skill = Mathf.Clamp(p.research_skill, 1, 10);
        stats.social_skill = Mathf.Clamp(p.social_skill, 1, 10);
        stats.research_ability_100 = Mathf.Clamp(p.research_skill * 10, 0, 100);
        stats.social_ability_100 = Mathf.Clamp(p.social_skill * 10, 0, 100);
        stats.current_week = Mathf.Max(1, p.current_week);
        stats.current_phase = string.IsNullOrEmpty(p.current_phase) ? "Morning" : p.current_phase;
        stats.total_days = Mathf.Max(0, p.total_days);
        GameHUD.Instance?.RefreshStats(stats);
        CheckWarnings();
    }

    public void ApplyFullPlayerResponse(PlayerDataResponse data, string rawJson)
    {
        if (data?.player != null) ApplyPlayerPayload(data.player);

        if (data?.time != null)
        {
            stats.server_week_name = data.time.week_name ?? "";
            stats.server_phase_name = data.time.phase_name ?? "";
            stats.current_week = data.time.current_week > 0 ? data.time.current_week : stats.current_week;
            stats.current_phase = data.time.current_phase ?? stats.current_phase;
            stats.total_days = data.time.total_days;
            GameHUD.Instance?.RefreshStats(stats);
        }

        ApplyFriendshipsFromRawJson(rawJson);
    }

    /// <summary>v2.1：将 GET /player 的 player 对象映射到本地 stats 与 HUD。</summary>
    public void ApplyPlayerStateV21(QinghuaStory.PlayerStateV21 p)
    {
        if (p == null) return;

        float prevTotal = stats.client_cached_total_game_minutes;
        stats.client_cached_game_hour = p.hour;
        stats.client_cached_game_minute = p.minute;
        stats.client_cached_total_game_minutes = p.total_game_minutes;

        stats.gpa = Mathf.Clamp(p.gpa, 0f, 4f);
        stats.energy = Mathf.Clamp(p.energy, 0, 100);
        stats.health = Mathf.Clamp(p.health, 0, 100);
        stats.research_ability_100 = Mathf.Clamp(p.research_ability, 0, 100);
        stats.social_ability_100 = Mathf.Clamp(p.social_ability, 0, 100);
        stats.research_skill = AbilityHundredToDec(p.research_ability);
        stats.social_skill = AbilityHundredToDec(p.social_ability);
        stats.math_skill = Mathf.Clamp(stats.math_skill, 1, 10);
        stats.english_skill = Mathf.Clamp(stats.english_skill, 1, 10);
        stats.srt_project = Mathf.Clamp(p.srt_project, 0, 1);
        stats.lab_status = string.IsNullOrEmpty(p.lab_status) ? "none" : p.lab_status;
        stats.current_week = Mathf.Max(1, p.current_week);
        stats.current_phase = string.IsNullOrEmpty(p.phase) ? stats.current_phase : p.phase;
        stats.server_week_name = string.IsNullOrEmpty(p.semester_name) ? stats.server_week_name : p.semester_name;
        stats.server_phase_name = string.IsNullOrEmpty(p.phase_name) ? stats.server_phase_name : p.phase_name;
        stats.server_date_display = p.date_display ?? "";
        stats.server_time_display = p.time_display ?? "";
        stats.semester_index = p.semester_index;
        stats.is_game_over_server = p.is_game_over;
        stats.failed_credits = Mathf.Max(0, p.failed_credits);
        stats.social_org = p.social_org ?? "";
        stats.social_rank = p.social_rank ?? "";
        TrackSemesterChange(p.semester_index, p.semester_name);
        GameHUD.Instance?.RefreshStats(stats);
        CheckWarnings();
        MealDeadlineCrossingDetector.NotifyTimeAdvanced(prevTotal, stats.client_cached_total_game_minutes);
    }

    private static int AbilityHundredToDec(int ability100)
    {
        if (ability100 <= 0) return 1;
        return Mathf.Clamp((ability100 + 9) / 10, 1, 10);
    }

    private void TrackSemesterChange(int semesterIndex, string semesterName)
    {
        if (!_semesterTracked)
        {
            _semesterTracked = true;
            _lastSemesterIndex = semesterIndex;
            return;
        }
        if (_lastSemesterIndex == semesterIndex) return;
        int prev = _lastSemesterIndex;
        _lastSemesterIndex = semesterIndex;

        void NotifyNewSemester()
        {
            SemesterIndexChanged?.Invoke(prev, semesterIndex);
            string name = string.IsNullOrEmpty(semesterName) ? $"学期 {semesterIndex}" : semesterName;
            if (!SuppressActivityHudToasts)
                GameHUD.Instance?.ShowNotification($"新学期：{name}（请选课/查看课表）", 6f);
        }

        var transcript = FindObjectOfType<SemesterTranscriptUI>(true);
        if (prev >= 0 && transcript != null)
            transcript.ShowForEndedSemester(prev, semesterIndex, semesterName, stats.gpa, NotifyNewSemester);
        else
            NotifyNewSemester();
    }

    public void ApplyFriendshipsFromRawJson(string rawJson)
    {
        var friendMap = PlayerResponseParser.ExtractFriendships(rawJson);
        _friendships.Clear();
        GameHUD.Instance?.ClearFriendshipsDisplay();
        foreach (var kv in friendMap)
            OnNpcFriendshipChanged(kv.Key, kv.Value);
    }

    /// <summary>活动 / 上课等返回的 v2.1 结果：状态、事件、解锁提示。</summary>
    /// <param name="executedActivityId">POST /activities/execute 的 activity_id（保留供调用方扩展）。</param>
    /// <param name="timeBeforeExecute">execute 前的 GET /time（保留参数，与旧食堂流程兼容）。</param>
    public void ProcessActivityResultV21(QinghuaStory.ActivityResultV21 r, string executedActivityId = null,
        QinghuaStory.TimeInfoV21 timeBeforeExecute = null)
    {
        if (r == null) return;
        if (r.new_state != null)
            ApplyPlayerStateV21(r.new_state);
        else
            RefreshFromServer();

        if (!SuppressActivityHudToasts && r.newly_unlocked != null)
        {
            foreach (var u in r.newly_unlocked)
                GameHUD.Instance?.ShowNotification($"解锁：{u}", 4f);
        }

        DispatchGameEventsV21(r.events);

        ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
        MealMissPenaltyMonitor.RequestImmediatePoll();
    }

    /// <summary>POST /player/penalties/meals 结果：应用 player 与 events（漏餐判定由服务端完成）。</summary>
    public void ProcessMealPenaltyResultV21(MealPenaltyResponseV21 r)
    {
        if (r == null || !r.success)
            return;
        if (r.player != null)
            ApplyPlayerStateV21(r.player);
        else if (r.applied)
            RefreshFromServer();

        if (r.applied && r.missed_meals != null && r.missed_meals.Length > 0)
            MealReminderUiGate.OfferShow(r.missed_meals, r.energy_delta, r.health_delta);

        DispatchGameEventsV21(r.events);

        ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
    }

    void DispatchGameEventsV21(QinghuaStory.GameEventV21[] events)
    {
        if (events == null) return;
        foreach (var ev in events)
        {
            if (ev == null || string.IsNullOrEmpty(ev.type)) continue;
            bool alwaysShow = ev.type == "faint" || ev.type == "hospital" ||
                              ev.type == "energy_warning" || ev.type == "health_warning";
            if (!alwaysShow && SuppressActivityHudToasts) continue;

            string msg = string.IsNullOrEmpty(ev.message) ? ev.type : ev.message;
            switch (ev.type)
            {
                case "faint":
                case "hospital":
                    GameHUD.Instance?.ShowNotification(msg, 7f);
                    break;
                case "energy_warning":
                case "health_warning":
                    GameHUD.Instance?.ShowNotification(msg, 4f);
                    break;
                default:
                    GameHUD.Instance?.ShowNotification(msg, 3f);
                    break;
            }
        }
    }

    public void ProcessAttendClassResultV21(QinghuaStory.AttendClassResponseV21 r)
    {
        if (r == null) return;

        if (r.new_time != null)
        {
            if (!string.IsNullOrEmpty(r.new_time.date_display))
                stats.server_date_display = r.new_time.date_display;
            if (!string.IsNullOrEmpty(r.new_time.time_display))
                stats.server_time_display = r.new_time.time_display;
            GameHUD.Instance?.RefreshStats(stats);
        }

        string statusCn = (r.attendance_status ?? "") switch
        {
            "on_time" => "出勤",
            "late" => "迟到",
            "absent" => "缺勤",
            _ => string.IsNullOrEmpty(r.attendance_status) ? "—" : r.attendance_status
        };
        string line = r.success
            ? $"{r.course_name} · {statusCn} · 掌握 {(r.mastery_delta >= 0 ? "+" : "")}{r.mastery_delta}"
            : "上课请求未完成";
        if (!SuppressActivityHudToasts)
            GameHUD.Instance?.ShowNotification(line, 4f);
        RefreshFromServer();
        ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
        MealMissPenaltyMonitor.RequestImmediatePoll();
    }

    // ══════════════════════════════════════════
    // 后端同步
    // ══════════════════════════════════════════

    private IEnumerator FetchPlayerStateRoutine(Action onDone = null)
    {
        if (APIManager.Instance == null)
        {
            Debug.LogWarning("[PlayerManager] 无法拉取玩家状态：APIManager 缺失");
            onDone?.Invoke();
            yield break;
        }

        string url = APIManager.ApiBase + "/player";
        string text = null;
        string err = null;
        yield return ApiTransport.SendGet(url, APIManager.ApiToken, (b, e) => { text = b; err = e; });

        if (!string.IsNullOrEmpty(err))
        {
            Debug.LogWarning("[PlayerManager] 拉取玩家状态失败: " + err);
            onDone?.Invoke();
            yield break;
        }

        try
        {
            var v21 = JsonUtility.FromJson<QinghuaStory.PlayerFullResponseV21>(text);
            if (v21?.player != null)
            {
                ApplyPlayerStateV21(v21.player);
                ApplyFriendshipsFromRawJson(text);
            }
            else
            {
                var legacy = JsonUtility.FromJson<PlayerDataResponse>(text);
                ApplyFullPlayerResponse(legacy, text);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayerManager] 解析玩家数据失败: " + e.Message);
        }

        DailyProgressBaseline.EnsureInitialized(this);
        onDone?.Invoke();
    }

    public float addgpa(float i)
    {
        stats.gpa += i;
        return stats.gpa;
    }

    public int addene(int i)
    {
        stats.energy += i;
        return stats.energy;
    }

    public int addhea(int i)
    {
        stats.health += i;
        return stats.health;
    }

    public int addmath(int i)
    {
        stats.math_skill += i;
        return stats.math_skill;
    }

    public int addeng(int i)
    {
        stats.english_skill += i;
        return stats.english_skill;
    }

    public int addres(int i)
    {
        stats.research_skill += i;
        return stats.research_skill;
    }

    public int addsoc(int i)
    {
        stats.social_skill += i;
        return stats.social_skill;
    }

    private void CheckWarnings()
    {
        if (SuppressActivityHudToasts) return;
        if (stats.energy < 30)
            GameHUD.Instance?.ShowNotification("⚡ 精力不足！去食堂吃饭补充能量！", 3f);
        if (stats.health < 30)
            GameHUD.Instance?.ShowNotification("❤️ 健康值偏低！注意休息！", 3f);
        if (stats.semester_index != 0 && stats.gpa < 2.0f)
            GameHUD.Instance?.ShowNotification("⚠️ 绩点危险！可能被导师约谈！", 4f);
    }
}

// ══════════════════════════════════════════════
// 数据结构
// ══════════════════════════════════════════════

[Serializable]
public class PlayerStatsData
{
    public float gpa = 2.0f;
    public int energy = 60;
    public int health = 60;
    public int wisdom = 0;
    public int math_skill = 1;
    public int english_skill = 1;
    public int research_skill = 1;
    public int social_skill = 1;
    /// <summary>与后端 research_ability 一致，0–100，用于 UI。</summary>
    public int research_ability_100;
    public int social_ability_100;
    /// <summary>0/1，是否承担 SRT 课题。</summary>
    public int srt_project;
    /// <summary>none / joined / published</summary>
    public string lab_status = "none";
    public int current_week = 1;
    public string current_phase = "Morning";
    public int total_days = 0;

    /// <summary>来自服务端 time.week_name，用于 HUD。</summary>
    public string server_week_name = "";
    /// <summary>来自服务端 time.phase_name。</summary>
    public string server_phase_name = "";
    /// <summary>v2.1 player.date_display / time_display</summary>
    public string server_date_display = "";
    public string server_time_display = "";
    /// <summary>v2.1：0=大一上；≥1=后续学期；-1=未从 v2.1 同步（legacy 按数值展示绩点）。</summary>
    public int semester_index = -1;
    public bool is_game_over_server;
    public int failed_credits;
    public string social_org = "";
    public string social_rank = "";

    /// <summary>用于宵禁：与 GET /player 或 GET /time 最后一次同步的游表时刻；-1 表示未知。</summary>
    public int client_cached_game_hour = -1;
    public int client_cached_game_minute;
    public float client_cached_total_game_minutes;

    /// <summary>与后端一致：第一学期（index 0）内不在 UI 展示累计绩点数值。</summary>
    public bool GpaUiDeferredFirstSemester => semester_index == 0;

    public string FormatGpaHudLine()
    {
        if (GpaUiDeferredFirstSemester) return "暂无";
        return $"GPA {gpa:F2}";
    }

    public string FormatGpaPanelLine()
    {
        if (GpaUiDeferredFirstSemester) return "绩点: 暂无";
        return $"绩点: {gpa:F2}";
    }
}
