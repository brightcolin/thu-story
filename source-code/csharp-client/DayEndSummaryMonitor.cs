using System.Collections;
using UnityEngine;
using QinghuaStory;

/// <summary>
/// 在宵禁窗口内（与 <see cref="LateNightCurfewMonitor"/> 同款判定）且玩家不忙时，
/// 每个游戏日块最多触发一次每日总结 UI。
/// </summary>
public class DayEndSummaryMonitor : MonoBehaviour
{
    [SerializeField] private float pollIntervalSeconds = 5f;
    [SerializeField] private float pollWhenBlockedSeconds = 2f;
    [SerializeField] private float retryCooldownRealtimeSeconds = 30f;
    [SerializeField] private bool logDiagnostics;

    [Tooltip("关闭后仅可通过 DailySummaryUI.OpenFromDayEnd() 等手动触发")]
    [SerializeField] private bool enableAutoTrigger = true;

    private float _timer;
    private bool _retryFast;
    private int _lastTriggerGameDayBlock = int.MinValue;
    private float _lastTriggerRealtime = float.NegativeInfinity;

    private playercontrol _player;

    private void Start()
    {
        CacheRefs();
        DailySummaryUI.EnsureExists();
    }

    private void CacheRefs()
    {
        if (_player == null) _player = FindObjectOfType<playercontrol>();
    }

    private void Update()
    {
        if (!enableAutoTrigger) return;

        float interval = _retryFast ? pollWhenBlockedSeconds : pollIntervalSeconds;
        _timer += Time.unscaledDeltaTime;
        if (_timer < interval) return;
        _timer = 0f;

        CacheRefs();
        if (_player == null) return;
        if (DailySummaryUI.IsOpen) return;

        APIManager.EnsureExists();
        if (APIManager.Instance == null) return;

        _retryFast = false;
        StartCoroutine(EvaluateAfterFetchTime());
    }

    private IEnumerator EvaluateAfterFetchTime()
    {
        bool done = false;
        TimeInfoV21 t = null;
        APIManager.Instance.GetTimeV21(
            x => { t = x; done = true; },
            _ => done = true);
        while (!done) yield return null;

        if (t == null || t.is_game_over) yield break;

        bool inCurfew = CurfewTimeV21.IsPastOneAmCurfew(t);
        int block = Mathf.FloorToInt(t.total_game_minutes / ActivitySceneIdsV21.GameDayMinutes);

        if (logDiagnostics)
        {
            Debug.Log(
                "[DayEndSummary] " +
                $"hour={t.hour} min={t.minute} inCurfew={inCurfew} block={block} " +
                $"busy={_player.IsActivityBusy} pres={ActivityPresentationUI.IsOpen} " +
                $"summaryOpen={DailySummaryUI.IsOpen}");
        }

        if (!inCurfew) yield break;

        if (_player.IsActivityBusy || ActivityPresentationUI.IsOpen)
        {
            _retryFast = true;
            yield break;
        }

        if (block == _lastTriggerGameDayBlock &&
            Time.realtimeSinceStartup - _lastTriggerRealtime < retryCooldownRealtimeSeconds)
            yield break;

        _lastTriggerGameDayBlock = block;
        _lastTriggerRealtime = Time.realtimeSinceStartup;
        DailySummaryUI.Instance?.OpenFromDayEnd();
    }
}
