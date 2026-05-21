using System;

namespace QinghuaStory
{
    /// <summary>
    /// 宵禁判断：仅使用 GET /time 返回的 <see cref="TimeInfoV21"/> 字段。
    /// </summary>
    public static class CurfewTimeV21
    {
        public static bool IsLateNightPhase(TimeInfoV21 t)
        {
            if (t == null) return false;
            if (!string.IsNullOrEmpty(t.phase) &&
                t.phase.Equals("Night", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(t.phase_name) && t.phase_name == "深夜")
                return true;
            return false;
        }

        /// <summary>与 GameTimeHUD 一致的后端展示修正：深夜且 hour==12 时按 0 点（后端约定）。</summary>
        public static int NormalizeHourForUi(TimeInfoV21 t)
        {
            if (t == null) return 0;
            int h = t.hour;
            if (IsLateNightPhase(t) && h == 12)
                h = 0;
            return h;
        }

        /// <summary>
        /// 后端 <c>total_game_minutes</c> 在当日内偏移（分钟）；日块长度与对接文档一致。
        /// </summary>
        public static int BackendGameDayOffsetMinutes(TimeInfoV21 t)
        {
            if (t == null) return 0;
            return BackendGameDayOffsetMinutesFromTotal(t.total_game_minutes);
        }

        /// <summary>仅用累计游戏分钟得到当日内从 6:30 起的偏移（与 <see cref="BackendGameDayOffsetMinutes(TimeInfoV21)"/> 一致）。</summary>
        public static int BackendGameDayOffsetMinutesFromTotal(float totalGameMinutes)
        {
            double frac = totalGameMinutes % ActivitySceneIdsV21.GameDayMinutes;
            if (frac < 0) frac += ActivitySceneIdsV21.GameDayMinutes;
            return (int)frac;
        }

        /// <summary>
        /// 次日 00:50 起至 6:30 前：若不在宿舍须强制回宿舍（0:00～0:49 可仍在校外）。
        /// 仅按钟点 + <c>hour==24</c> 判定；勿单独用 <c>total_game_minutes</c> 块边界，其与墙上 6:30 不对齐时会在晚间误判（如 22:00）。
        /// </summary>
        public static bool IsPastOneAmCurfew(TimeInfoV21 t)
        {
            if (t == null || t.is_game_over) return false;

            float total = t.total_game_minutes;
            bool totalUsable = !float.IsNaN(total) && !float.IsInfinity(total) && total >= 0f;
            int delta = 0;
            bool deltaHit = false;
            if (totalUsable)
            {
                delta = ActivitySceneIdsV21.MinutesToNextGameDayBlockStart(t);
                deltaHit = delta >= 1 && delta <= ActivitySceneIdsV21.CurfewMaxMinutesBeforeNextBlockStart;
            }

            if (t.hour == 24)
            {
                // #region agent log
                DebugSessionNdjson.CurfewDecision(
                    "H2", "branch_hour24", t.hour, t.minute, total, delta, totalUsable,
                    deltaHit, true, false, true, t.phase, t.phase_name);
                // #endregion
                return true;
            }

            bool hourOnlyHit = IsPastOneAmCurfewHourOnly(t);

            // #region agent log
            if (deltaHit && !hourOnlyHit)
            {
                DebugSessionNdjson.CurfewDecision(
                    "H1", "postfix_suppressed_delta_without_wall_curfew",
                    t.hour, t.minute, total, delta, totalUsable,
                    true, false, false, false, t.phase, t.phase_name);
            }

            if (hourOnlyHit)
            {
                DebugSessionNdjson.CurfewDecision(
                    "H2", "branch_hour_only", t.hour, t.minute, total, delta, totalUsable,
                    deltaHit, false, true, true, t.phase, t.phase_name);
            }
            // #endregion

            return hourOnlyHit;
        }

        /// <summary>与 GameTimeHUD 一致的 hour/minute 宵禁推断（不含 hour==24 特例）。</summary>
        private static bool IsPastOneAmCurfewHourOnly(TimeInfoV21 t)
        {
            int h = NormalizeHourForUi(t);
            int m = t.minute;

            if (h > 6 || (h == 6 && m >= 30))
                return false;

            if (h > 1)
                return h < 6 || (h == 6 && m < 30);
            if (h == 1)
                return true;

            // h == 0：仅 00:50～00:59 算宵禁
            return h == 0 && m >= 50;
        }
    }
}
