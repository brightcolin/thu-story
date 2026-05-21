using System.Collections;
using UnityEngine;

/// <summary>
/// 缺餐条：活动结算全屏或 SuppressActivityHudToasts 期间暂存，关闭/解除后再展示。
/// </summary>
public static class MealReminderUiGate
{
    private static MonoBehaviour _runner;
    private static Coroutine _waitCo;

    private static bool _hasPending;
    private static string[] _pendingMissed;
    private static int _pendingEnergyDelta;
    private static int _pendingHealthDelta;

    public static void SetRunner(MonoBehaviour host)
    {
        _runner = host;
    }

    public static void InstallPresentationClosedHook()
    {
        ActivityPresentationUI.FullyClosed -= OnPresentationFullyClosed;
        ActivityPresentationUI.FullyClosed += OnPresentationFullyClosed;
    }

    public static void UninstallPresentationClosedHook()
    {
        ActivityPresentationUI.FullyClosed -= OnPresentationFullyClosed;
    }

    private static void OnPresentationFullyClosed() => StartWaitIfNeeded();

    private static bool IsBlocked() =>
        ActivityPresentationUI.IsOpen || PlayerManager.SuppressActivityHudToasts;

    /// <summary>缺餐扣罚且需要展示时调用（状态已由 ProcessMealPenaltyResultV21 应用完毕）。</summary>
    public static void OfferShow(string[] missedMeals, int energyDelta, int healthDelta)
    {
        if (missedMeals == null || missedMeals.Length == 0)
            return;

        MealMissUIPanel.EnsureExists(GameHUD.Instance != null ? GameHUD.Instance.hudFont : null);

        if (!IsBlocked())
        {
            MealMissUIPanel.Instance?.Show(missedMeals, energyDelta, healthDelta);
            return;
        }

        _pendingMissed = missedMeals;
        _pendingEnergyDelta = energyDelta;
        _pendingHealthDelta = healthDelta;
        _hasPending = true;
        StartWaitIfNeeded();
    }

    private static void StartWaitIfNeeded()
    {
        if (!_hasPending) return;
        var host = _runner != null ? _runner : PlayerManager.Instance;
        if (host == null) return;
        if (_waitCo != null) return;
        _waitCo = host.StartCoroutine(WaitUntilUnblockedAndShowRoutine());
    }

    private static IEnumerator WaitUntilUnblockedAndShowRoutine()
    {
        while (true)
        {
            while (_hasPending && IsBlocked())
                yield return null;

            if (!_hasPending)
                break;

            string[] m = _pendingMissed;
            int e = _pendingEnergyDelta;
            int h = _pendingHealthDelta;
            _hasPending = false;
            _pendingMissed = null;

            if (m != null && m.Length > 0)
            {
                if (!IsBlocked())
                    MealMissUIPanel.Instance?.Show(m, e, h);
                else
                {
                    _pendingMissed = m;
                    _pendingEnergyDelta = e;
                    _pendingHealthDelta = h;
                    _hasPending = true;
                    continue;
                }
            }

            if (!_hasPending)
                break;
        }

        _waitCo = null;
    }
}
