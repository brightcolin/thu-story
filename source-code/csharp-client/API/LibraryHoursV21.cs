namespace QinghuaStory
{
    /// <summary>
    /// 图书馆开馆：与 HUD/宵禁一致使用 <see cref="TimeInfoV21"/>；开馆 8:00–22:00（含 8:00，不含 22:00）。
    /// </summary>
    public static class LibraryHoursV21
    {
        public const int OpenStartMinutes = 8 * 60;
        public const int OpenEndMinutes = 22 * 60;

        public static bool IsLibraryClosed(TimeInfoV21 t)
        {
            if (t == null || t.is_game_over)
                return false;

            if (t.hour == 24)
                return true;

            int h = CurfewTimeV21.NormalizeHourForUi(t);
            int m = t.minute;
            int minutes = h * 60 + m;
            return minutes < OpenStartMinutes || minutes >= OpenEndMinutes;
        }

        /// <summary>
        /// 用 PlayerManager 缓存合成时刻；缓存无效时返回 false（不把 <paramref name="closed"/> 当真）。
        /// </summary>
        public static bool TryIsLibraryClosedFromPlayerCache(out bool closed)
        {
            closed = false;
            var pm = PlayerManager.Instance;
            if (pm?.stats == null || pm.stats.client_cached_game_hour < 0)
                return false;

            var t = new TimeInfoV21
            {
                hour = pm.stats.client_cached_game_hour,
                minute = pm.stats.client_cached_game_minute,
                total_game_minutes = pm.stats.client_cached_total_game_minutes,
                phase = pm.stats.current_phase,
                phase_name = pm.stats.server_phase_name,
                is_game_over = pm.stats.is_game_over_server
            };
            closed = IsLibraryClosed(t);
            return true;
        }
    }
}
