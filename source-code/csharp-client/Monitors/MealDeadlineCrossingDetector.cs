using UnityEngine;
using QinghuaStory;

/// <summary>
/// 根据《前端对接说明》餐段截止（从当日 6:30 起偏移 210/450/870 分 ≈ 10:00/14:00/21:00），
/// 在客户端时间向前推进时检测是否越过任一边界；仅用于触发 POST /player/penalties/meals，扣罚以服务端为准。
/// </summary>
public static class MealDeadlineCrossingDetector
{
    private static readonly int[] DeadlineOffsetsWithinDay = { 210, 450, 870 };

    /// <param name="prevTotalGameMinutes">更新前 stats.client_cached_total_game_minutes，未知时用 &lt; 0</param>
    /// <param name="newTotalGameMinutes">更新后的 total_game_minutes</param>
    public static void NotifyTimeAdvanced(float prevTotalGameMinutes, float newTotalGameMinutes)
    {
        if (newTotalGameMinutes < 0f)
            return;

        if (prevTotalGameMinutes >= 0f && newTotalGameMinutes < prevTotalGameMinutes - 0.5f)
            return;

        if (prevTotalGameMinutes < 0f)
            return;

        if (newTotalGameMinutes <= prevTotalGameMinutes + 0.001f)
            return;

        if (!CrossedAnyDeadline(prevTotalGameMinutes, newTotalGameMinutes))
            return;

        if (ServerPauseCoordinator.Depth > 0)
            return;

        MealMissPenaltyMonitor.RequestImmediatePoll();
    }

    private static bool CrossedAnyDeadline(float prev, float curr)
    {
        int day = ActivitySceneIdsV21.GameDayMinutes;
        long b0 = (long)System.Math.Floor(prev / day) * day;
        long b1 = (long)System.Math.Floor(curr / day) * day;
        for (long blockStart = b0; blockStart <= b1; blockStart += day)
        {
            foreach (int off in DeadlineOffsetsWithinDay)
            {
                float boundary = blockStart + off;
                if (prev < boundary && curr >= boundary)
                    return true;
            }
        }
        return false;
    }
}
