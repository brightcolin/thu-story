using System;

namespace QinghuaStory
{
    /// <summary>
    /// 与《活动与结局参考》《游戏玩法说明》对齐的本地解锁/开放条件提示；用于活动失败 UI 附加说明。
    /// </summary>
    public static class ActivityUnlockHints
    {
        /// <summary>闭馆、禁止自习等：无标准 activity_id 时使用。</summary>
        public const string LibraryClosedContext = "library_closed";

        /// <summary>图书馆场景内因闭馆无法社团活动等。</summary>
        public const string LibraryClubClosedContext = "library_club_closed";

        /// <summary>POST /class/attend 等非 /activities/execute。</summary>
        public const string AttendClassContext = "attend_class";

        /// <summary>v2.1 Execute 宵禁睡眠等带 curfew 标志，与日常 sleep 条件一致。</summary>
        public const string SleepCurfewContext = "sleep_curfew";

        const string SectionBreak = "\n\n────────\n开放条件提示：\n";

        public static string AppendUnlockHint(string message, string activityIdForHint)
        {
            if (string.IsNullOrEmpty(message)) message = "";
            string hint = HintFor(activityIdForHint);
            if (string.IsNullOrEmpty(hint)) return message;
            if (message.IndexOf("开放条件提示", StringComparison.Ordinal) >= 0)
                return message;
            return message + SectionBreak + hint;
        }

        public static string HintOnly(string activityIdForHint)
        {
            return HintFor(activityIdForHint) ?? "";
        }

        static string HintFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (string.Equals(key, LibraryClosedContext, StringComparison.Ordinal))
                return "图书馆建筑开馆时间为每日 8:00–22:00。开馆后可在馆内使用自习等功能；自习活动本身还须满足游戏内时段（见下一条）与 GET /activities 列表。\n"
                     + "图书馆自习（后端时段）：上午场 6:00–12:00、下午场 12:00–17:00、晚上场 17:00–22:00；最低精力 20。";

            if (string.Equals(key, LibraryClubClosedContext, StringComparison.Ordinal))
                return "社团活动须在开放时段（常见 13:00–21:00）且活动出现在 GET /activities 中；若在闭馆时被传送出馆，请开馆后再进入图书馆尝试。\n"
                     + "解锁「科协活动」标志 club_joined：社工能力 ≥ 20 且张锟霖好感 ≥ 30（自动检测）。";

            if (string.Equals(key, AttendClassContext, StringComparison.Ordinal))
                return "请在课表对应「星期 + 节次」进入教室场景，在可互动状态下按 E 上课；准时/迟到/缺勤由服务端结合当前时刻判定。掌握度与课表见菜单「课程掌握度」「我的课表」。";

            if (string.Equals(key, SleepCurfewContext, StringComparison.Ordinal))
                return "日常「睡觉」开放时段为 21:00 起至次日 1:00 前，每日最多 1 次，会睡到次日 6:30；宵禁流程由服务端强制结算，条件以后端为准。";

            if (key.StartsWith("study_library_", StringComparison.Ordinal))
                return "须在对应场次时段且该自习 id 出现在 GET /activities 中：上午场约 6:00–12:00、下午场 12:00–17:00、晚上场 17:00–22:00；最低精力 20。进入图书馆建筑需开馆 8:00–22:00。可选传 course_id 以增加该课掌握度。";

            if (key.StartsWith("research_", StringComparison.Ordinal))
                return "需已解锁实验室标志 lab_access：科研能力 ≥ 30 且林晚晴好感 ≥ 40（自动检测）；时段：上午 6:00–12:00、下午 12:00–17:00、晚上 17:00–22:00；最低精力 25。";

            switch (key)
            {
                case "club_activity":
                    return "时段常见 13:00–21:00；需解锁 club_joined（科协活动）：社工能力 ≥ 20 且张锟霖好感 ≥ 30；还须出现在 GET /activities 且满足精力等。";
                case "date_boyfriend":
                    return "时段常见 12:00–22:00，每日最多 1 次；需解锁 boyfriend_unlocked：沈星辞好感 ≥ 60。";
                case "social_meeting":
                    return "时段常见 18:00–21:00，每日最多 1 次；需解锁 social_org_joined：在社工系统中成功加入任一组织。";
                case "consult_mentor":
                    return "时段常见 13:00–17:00，每日最多 1 次；需与 npc 林晚晴关联解锁；最低精力以服务端为准。";
                case "consult_teacher":
                    return "时段常见 8:00–17:00，每日最多 1 次；需与 npc 王老师（王玉霞）关联；精力 -10。";
                case "chat_roommate":
                    return "时段常见 18:00 至次日 1:00 前；需与 npc 陈奕然关联。";
                case "help_tourist":
                    return "时段 8:00–17:00，每日最多 2 次；需与 npc 赵晓关联。";
                case "eat_canteen":
                    return "全天可尝试；每日最多 3 餐；需与 npc 李娟关联。成功后会登记餐段，减少缺餐惩罚。";
                case "rest":
                    return "全天；休息约 60 分钟，精力 +30、健康 +10。";
                case "sleep":
                    return "时段 21:00 至次日 1:00 前，睡到次日 6:30；每日最多 1 次。";
                case "exercise":
                    return "时段 6:00–21:00；最低精力 15；运动约 60 分钟。";
                case "tour_campus":
                    return "时段常见 8:00–18:00；游览约 60 分钟。";
                default:
                    return null;
            }
        }
    }
}
