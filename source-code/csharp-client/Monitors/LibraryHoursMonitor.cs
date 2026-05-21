using UnityEngine;
using QinghuaStory;

/// <summary>
/// 闭馆时段在馆内（scene 11）时强制传送至馆外（scene 6）；时间与 <see cref="LibraryHoursV21"/> / GET /time 一致。
/// </summary>
public class LibraryHoursMonitor : MonoBehaviour
{
    [SerializeField] private float pollIntervalSeconds = 5f;
    [SerializeField] private float pollWhenBlockedSeconds = 2f;
    [SerializeField] private float retryCooldownRealtimeSeconds = 45f;
    [SerializeField] private bool logDiagnostics;

    private float _timer;
    private bool _blockedFastRetry;
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
        float interval = _blockedFastRetry ? pollWhenBlockedSeconds : pollIntervalSeconds;
        _timer += Time.unscaledDeltaTime;
        if (_timer < interval) return;
        _timer = 0f;

        CacheRefs();
        if (_player == null || _tr == null) return;

        APIManager.EnsureExists();
        if (APIManager.Instance != null)
            APIManager.Instance.GetTimeV21(EvaluateTime, _ => TryEvaluateFromCachedTime());
        else
            TryEvaluateFromCachedTime();
    }

    public void NotifyLibraryHoursTeleportCommitted(int gameDayBlock)
    {
        _lastTriggerGameDayBlock = gameDayBlock;
        _lastTriggerRealtime = Time.realtimeSinceStartup;
    }

    private void TryEvaluateFromCachedTime()
    {
        var pm = PlayerManager.Instance;
        if (pm?.stats == null || pm.stats.client_cached_game_hour < 0)
            return;

        var t = new TimeInfoV21
        {
            hour = pm.stats.client_cached_game_hour,
            minute = pm.stats.client_cached_game_minute,
            total_game_minutes = pm.stats.client_cached_total_game_minutes,
            phase = pm.stats.current_phase,
            phase_name = pm.stats.server_phase_name,
            is_game_over = pm.stats.is_game_over_server
        };
        EvaluateTime(t);
    }

    private void EvaluateTime(TimeInfoV21 t)
    {
        if (t == null || t.is_game_over) return;

        _blockedFastRetry = false;

        if (_tr.scene != 11) return;

        bool closed = LibraryHoursV21.IsLibraryClosed(t);
        if (!closed) return;

        LibrarySelfStudyUI.CloseIfOpenForScenePolicy();
        SceneRActivityUI.CloseIfOpenForScenePolicy();

        int block = Mathf.FloorToInt(t.total_game_minutes / ActivitySceneIdsV21.GameDayMinutes);

        if (logDiagnostics)
        {
            Debug.Log(
                "[LibraryHours] " +
                $"hour={t.hour} min={t.minute} scene={_tr.scene} " +
                $"busy={_player.IsActivityBusy} presUI={ActivityPresentationUI.IsOpen} block={block}");
        }

        if (_player.IsActivityBusy || ActivityPresentationUI.IsOpen)
        {
            _blockedFastRetry = true;
            return;
        }

        if (block == _lastTriggerGameDayBlock &&
            Time.realtimeSinceStartup - _lastTriggerRealtime < retryCooldownRealtimeSeconds)
            return;

        _player.RequestEjectFromLibraryWhenClosed(block);
    }
}
