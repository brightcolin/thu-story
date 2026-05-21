using UnityEngine;
using QinghuaStory;

/// <summary>
/// 次日 00:50 起至 6:30 前不在宿舍则强制送回：优先 GET /time；失败或无 APIManager 时用 PlayerManager 缓存的游表时刻（由 /player 或 HUD 拉时更新）。
/// </summary>
public class LateNightCurfewMonitor : MonoBehaviour
{
    [Tooltip("轮询间隔（秒，不受 timeScale 影响）；宵禁窗口内可适当缩短以免漏判")]
    [SerializeField] private float pollIntervalSeconds = 5f;

    [Tooltip("已入宵禁但被活动/结算 UI 挡住时的重试间隔（秒），避免 15s 空窗一直逮不到关 UI 的瞬间")]
    [SerializeField] private float pollWhenCurfewBlockedSeconds = 2f;

    [Tooltip("同一游戏日内触发成功后，最短多少秒可再试（避免死循环请求）")]
    [SerializeField] private float retryCooldownRealtimeSeconds = 45f;

    [Tooltip("勾选后在 Console 输出宵禁判定与跳过原因（GET /time 的 hour/minute/phase、total、delta、scene）")]
    [SerializeField] private bool logCurfewDiagnostics;

    private float _timer;
    private bool _curfewRetryFast;
    private int _lastTriggerGameDayBlock = int.MinValue;
    private float _lastTriggerRealtime = float.NegativeInfinity;

    private playercontrol _player;
    private scenetrans _tr;

    private void Start()
    {
        CacheRefs();
    }

    private void CacheRefs()
    {
        if (_player == null) _player = GetComponent<playercontrol>();
        if (_player == null) _player = FindObjectOfType<playercontrol>();
        if (_tr == null) _tr = FindObjectOfType<scenetrans>();
    }

    private void Update()
    {
        float interval = _curfewRetryFast ? pollWhenCurfewBlockedSeconds : pollIntervalSeconds;
        _timer += Time.unscaledDeltaTime;
        if (_timer < interval) return;
        _timer = 0f;

        CacheRefs();
        if (_player == null || _tr == null) return;

        APIManager.EnsureExists();
        if (APIManager.Instance != null)
            APIManager.Instance.GetTimeV21(t => EvaluateTime(t, "api"), _ => TryEvaluateFromCachedTime("api_error"));
        else
            TryEvaluateFromCachedTime("no_apimanager");
    }

    private void TryEvaluateFromCachedTime(string timeSourceTag)
    {
        var pm = PlayerManager.Instance;
        if (pm?.stats == null || pm.stats.client_cached_game_hour < 0)
        {
            // #region agent log
            DebugSessionNdjson.CurfewMonitorEval(
                "H5", timeSourceTag, "skip_cache_invalid", false,
                pm?.stats?.client_cached_game_hour ?? -1, 0, _tr != null ? _tr.scene : -1,
                _player != null && _player.IsActivityBusy,
                ActivityPresentationUI.IsOpen, -1);
            // #endregion
            return;
        }

        var t = new TimeInfoV21
        {
            hour = pm.stats.client_cached_game_hour,
            minute = pm.stats.client_cached_game_minute,
            total_game_minutes = pm.stats.client_cached_total_game_minutes,
            phase = pm.stats.current_phase,
            phase_name = pm.stats.server_phase_name,
            is_game_over = pm.stats.is_game_over_server
        };
        EvaluateTime(t, "cache");
    }

    /// <summary>在强制回宿舍已实际执行传送后由 <see cref="playercontrol"/> 调用，用于防抖（避免协程开头 yield break 仍锁冷却）。</summary>
    public void NotifyCurfewTeleportCommitted(int gameDayBlock)
    {
        _lastTriggerGameDayBlock = gameDayBlock;
        _lastTriggerRealtime = Time.realtimeSinceStartup;
    }

    private void EvaluateTime(TimeInfoV21 t, string timeSource)
    {
        if (t == null || t.is_game_over) return;

        _curfewRetryFast = false;

        bool inCurfew = CurfewTimeV21.IsPastOneAmCurfew(t);
        int deltaNext = ActivitySceneIdsV21.MinutesToNextGameDayBlockStart(t);
        int block = Mathf.FloorToInt(t.total_game_minutes / ActivitySceneIdsV21.GameDayMinutes);

        if (logCurfewDiagnostics)
        {
            Debug.Log(
                "[LateNightCurfew] " +
                $"hour={t.hour} min={t.minute} phase={t.phase}/{t.phase_name} total={t.total_game_minutes} " +
                $"deltaNext6:30={deltaNext} block={block} scene={_tr.scene} busy={_player.IsActivityBusy} " +
                $"presUI={ActivityPresentationUI.IsOpen} inCurfew={inCurfew}");
        }

        void LogMonitor(string outcome, bool ic, bool setFastRetry)
        {
            if (setFastRetry) _curfewRetryFast = true;
            // #region agent log
            if (ic || outcome != "not_in_curfew")
            {
                DebugSessionNdjson.CurfewMonitorEval(
                    "H6", timeSource, outcome, ic,
                    t.hour, t.minute, _tr.scene,
                    _player.IsActivityBusy, ActivityPresentationUI.IsOpen,
                    block);
            }
            // #endregion
        }

        if (!inCurfew)
        {
            LogMonitor("not_in_curfew", false, false);
            return;
        }

        if (_tr.scene == 9)
        {
            if (logCurfewDiagnostics)
                Debug.Log("[LateNightCurfew] skip: already dorm scene 9");
            LogMonitor("skip_dorm", true, false);
            return;
        }

        if (_player.IsActivityBusy || ActivityPresentationUI.IsOpen)
        {
            if (logCurfewDiagnostics)
                Debug.Log("[LateNightCurfew] skip: activity busy or presentation UI open");
            LogMonitor("skip_busy_ui", true, true);
            return;
        }

        if (block == _lastTriggerGameDayBlock &&
            Time.realtimeSinceStartup - _lastTriggerRealtime < retryCooldownRealtimeSeconds)
        {
            if (logCurfewDiagnostics)
                Debug.Log("[LateNightCurfew] skip: debounce cooldown");
            LogMonitor("skip_debounce", true, false);
            return;
        }

        if (logCurfewDiagnostics)
            Debug.Log("[LateNightCurfew] invoking RequestForcedDormSleepFromCurfew block=" + block);

        LogMonitor("invoked", true, false);
        _player.RequestForcedDormSleepFromCurfew(block);
    }
}
