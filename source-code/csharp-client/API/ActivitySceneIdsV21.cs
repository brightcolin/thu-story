using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// 与《前端对接指南 v2.1》6.2 / 6.3 节一致：场景触发的 activity_id 必须属于后端当前开放集合。
    /// 自习/实验/社团等依赖时段，需按 GET /time 结果选用对应 id。
    /// </summary>
    public static class ActivitySceneIdsV21
    {
        public static string LibraryStudyFromServerTime(TimeInfoV21 t)
        {
            if (t == null) return "study_library_morning";
            int h = t.hour;
            if (h < 6) return "rest";
            if (h < 12) return "study_library_morning";
            if (h < 17) return "study_library_afternoon";
            if (h < 22) return "study_library_evening";
            return "rest";
        }

        public static string ResearchLabFromServerTime(TimeInfoV21 t)
        {
            if (t == null) return "research_morning";
            int h = t.hour;
            if (h < 6) return "rest";
            if (h < 12) return "research_morning";
            if (h < 17) return "research_afternoon";
            if (h < 22) return "research_evening";
            return "rest";
        }

        public static string ClubOrSocialFromServerTime(TimeInfoV21 t)
        {
            if (t == null) return "club_activity";
            int h = t.hour;
            if (h >= 13 && h < 22) return "club_activity";
            if (h >= 8 && h < 17) return "help_tourist";
            if (h >= 18) return "chat_roommate";
            return "rest";
        }

        /// <summary>second 场景（v2.1 §6.2）：游览校园，8–18 时可用。</summary>
        public static string TourCampusFromServerTime(TimeInfoV21 t)
        {
            if (t == null) return "";
            int h = t.hour;
            if (h >= 8 && h < 19) return "tour_campus";
            return "";
        }

        /// <summary>beauty 场景（v2.1 §6.2）：帮助游客，8–17 时可用。</summary>
        public static string HelpTouristFromServerTime(TimeInfoV21 t)
        {
            if (t == null) return "";
            int h = t.hour;
            if (h >= 8 && h < 18) return "help_tourist";
            return "";
        }

        /// <summary>后端 game 日长度（与对接文档一致）：6:30—次日 1:00，共 1110 分钟；与 GET /time 的 total_game_minutes 对齐使用。</summary>
        public const int GameDayMinutes = 1110;

        /// <summary>
        /// 宵禁：距下一 game 日 6:30 块起点剩余分钟数在 [1, 本值] 内时视为在宵禁窗口（约 00:50～6:29，共 340 分钟）。
        /// </summary>
        public const int CurfewMaxMinutesBeforeNextBlockStart = 340;

        /// <summary>
        /// 从当前 <c>total_game_minutes</c> 推进到下一游戏日 6:30（下一 1110 分钟块起点）所需的分钟数；
        /// 与夜间睡觉对齐目标一致，不调用活动接口时用于 <c>POST /time/advance?minutes=</c>。
        /// </summary>
        public static int MinutesToNextGameDayBlockStart(TimeInfoV21 t)
        {
            if (t == null) return 0;
            double blockFloor = System.Math.Floor(t.total_game_minutes / GameDayMinutes) * GameDayMinutes;
            float target = (float)(blockFloor + GameDayMinutes);
            int delta = Mathf.RoundToInt(target - t.total_game_minutes);
            return delta > 0 ? delta : 0;
        }

        /// <summary>
        /// 宿舍睡觉/休息后，将时钟对齐到目标：21 点前（且 6 点以后算同日午觉）仅推进 1 小时；
        /// 21 点后或凌晨 6 点前睡觉则对齐到「下一 game 日」6:30（即下一个 1110 分钟块起点）。
        /// 若服务端已通过活动推进到位，返回 0，避免重复叠加。
        /// </summary>
        public static int DormActivityCorrectionMinutes(TimeInfoV21 timeBefore, TimeInfoV21 timeAfter)
        {
            if (timeBefore == null || timeAfter == null) return 0;
            bool nightSleepJump = timeBefore.hour >= 21 || timeBefore.hour < 6;
            float target;
            if (nightSleepJump)
            {
                double block = System.Math.Floor(timeBefore.total_game_minutes / GameDayMinutes) * GameDayMinutes;
                target = (float)(block + GameDayMinutes);
            }
            else
                target = timeBefore.total_game_minutes + 60f;

            int delta = Mathf.RoundToInt(target - timeAfter.total_game_minutes);
            return delta > 0 ? delta : 0;
        }

        public static string DormSleepOrRestFromServerTime(TimeInfoV21 t)
        {
            if (t == null) return "rest";
            int h = t.hour;
            if (h >= 21 || h < 6) return "sleep";
            return "rest";
        }
    }
}
