using System;

namespace QinghuaStory
{
    // =========================================================
    //  v2.1 后端数据模型（字段名必须与 JSON 完全一致）
    //  本地后端默认值: http://127.0.0.1:8000
    //  Header: X-Token（由 APIManager Inspector 或 THUSTORY_API_TOKEN 配置）
    // =========================================================

    // ═══════════════════════════════════════
    //  时间系统
    // ═══════════════════════════════════════

    [Serializable]
    public class TimeInfoV21
    {
        public float total_game_minutes;
        public int semester_index;
        public string semester_name;
        public int week;
        public int day_in_semester;
        public int weekday;
        public string weekday_name;
        public int hour;
        public int minute;
        public string phase;
        public string phase_name;
        public bool is_game_over;
        public int total_days_elapsed;
        public string date_display;
        public string time_display;
    }

    [Serializable]
    public class PauseResumeResponseV21
    {
        public bool paused;
        public TimeInfoV21 time;
    }

    [Serializable]
    public class NextDayResponseV21
    {
        public TimeInfoV21 time;
    }

    // ═══════════════════════════════════════
    //  玩家状态
    // ═══════════════════════════════════════

    [Serializable]
    public class PlayerStateV21
    {
        public float total_game_minutes;
        public int is_paused;
        public int energy;
        public int health;
        public int research_ability;
        public int social_ability;
        public string social_org;
        public string social_rank;
        public int srt_project;
        public string lab_status;
        public int failed_credits;
        public float gpa;

        public int semester_index;
        public string semester_name;
        public int current_week;
        public int weekday;
        public string weekday_name;
        public int hour;
        public int minute;
        public string phase;
        public string phase_name;
        public bool is_game_over;
        public string date_display;
        public string time_display;
    }

    [Serializable]
    public class PlayerFullResponseV21
    {
        public PlayerStateV21 player;
        // friendships / unlocks / courses 等动态字段可按需使用 JSON.NET 或拆端点获取
    }

    // ═══════════════════════════════════════
    //  活动系统
    // ═══════════════════════════════════════

    [Serializable]
    public class ActivityEffectPreviewV21
    {
        public int energy;
        public int health;
        public int research_ability;
        public int social_ability;
    }

    [Serializable]
    public class ActivityInfoV21
    {
        public string id;
        public string name;
        public string description;
        public string npc_id;
        public string time_cost;
        public ActivityEffectPreviewV21 effect_preview;
    }

    [Serializable]
    public class ActivitiesResponseV21
    {
        public TimeInfoV21 time;
        public ActivityInfoV21[] activities;
    }

    [Serializable]
    public class NewTimeInfoV21
    {
        public string date_display;
        public string time_display;
        public int hour;
        public int minute;
    }

    [Serializable]
    public class GameEventV21
    {
        public string type;
        public string message;
    }

    [Serializable]
    public class ActivityResultV21
    {
        public bool success;
        public string activity_name;
        public string npc_id;
        /// <summary>自习等活动携带科目时，服务端返回（Json 字段 course_id）。</summary>
        public string course_id;
        /// <summary>与 course_id 对应的课程名（Json 字段 course_name）。</summary>
        public string course_name;
        /// <summary>本次活动引起的掌握度变化（Json 字段 mastery_delta）。</summary>
        public int mastery_delta;
        public int time_advanced_minutes;
        public NewTimeInfoV21 new_time;
        public PlayerStateV21 new_state;
        public string[] newly_unlocked;
        public GameEventV21[] events;
    }

    // ═══════════════════════════════════════
    //  NPC 对话
    // ═══════════════════════════════════════

    [Serializable]
    public class NPCInfoV21
    {
        public string npc_id;
        public string name;
        public string identity;
        public int friendship;
        public string friendship_tier;
    }

    [Serializable]
    public class NPCListResponseV21
    {
        public NPCInfoV21[] npcs;
    }

    [Serializable]
    public class ChatResponseV21
    {
        public string npc_name;
        public string reply;
        public string emotion;
        public int friendship_change;
        public int current_friendship;
        public string friendship_tier;
        public string[] newly_unlocked;
    }

    // ═══════════════════════════════════════
    //  课程系统
    // ═══════════════════════════════════════

    [Serializable]
    public class CourseInfoV21
    {
        public string course_id;
        public string course_name;
        public int credits;
        public string course_type;
        public int semester_index;
        public string description;
    }

    [Serializable]
    public class CoursesAvailableResponseV21
    {
        public int semester_index;
        public string semester_name;
        public CourseInfoV21[] courses;
    }

    [Serializable]
    public class ScheduleSlotV21
    {
        public int day_of_week;
        public int period;
        public string course_id;
        public string course_name;
        public int credits;
    }

    [Serializable]
    public class ScheduleResponseV21
    {
        public ScheduleSlotV21[] schedule;
    }

    [Serializable]
    public class SelectCourseResponseV21
    {
        public bool success;
        public string course_id;
    }

    [Serializable]
    public class PlayerCourseV21
    {
        public string course_id;
        public string course_name;
        public int credits;
        public float mastery;
        public int attendance_count;
        public int absence_count;
    }

    [Serializable]
    public class MyCoursesResponseV21
    {
        public PlayerCourseV21[] courses;
    }

    [Serializable]
    public class AttendClassResponseV21
    {
        public bool success;
        public string course_name;
        public string attendance_status;
        public int mastery_delta;
        public int time_advanced_minutes;
        public NewTimeInfoV21 new_time;
    }

    /// <summary>POST /player/penalties/meals（v2.2）。</summary>
    [Serializable]
    public class MealPenaltyResponseV21
    {
        public bool success;
        public bool applied;
        public string[] missed_meals;
        public int energy_delta;
        public int health_delta;
        public PlayerStateV21 player;
        public GameEventV21[] events;
    }

    // ═══════════════════════════════════════
    //  社工系统
    // ═══════════════════════════════════════

    [Serializable]
    public class SocialOrgInfoV21
    {
        public string id;
        public string name;
        public string bonus;
    }

    [Serializable]
    public class SocialOrgsResponseV21
    {
        public SocialOrgInfoV21[] orgs;
    }

    [Serializable]
    public class JoinOrgResponseV21
    {
        public bool success;
        public string org;
        public string rank;
        public string bonus;
    }

    [Serializable]
    public class PromoteResponseV21
    {
        public bool success;
        public string new_rank;
        public string message;
    }

    [Serializable]
    public class SocialStatusResponseV21
    {
        public string org;
        public string org_name;
        public string rank;
        public string rank_name;
        public int social_ability;
    }

    // ═══════════════════════════════════════
    //  结局系统
    // ═══════════════════════════════════════

    [Serializable]
    public class EndingInfoV21
    {
        public string id;
        public string name;
        public bool available;
        public bool forced;
    }

    [Serializable]
    public class EndingsResponseV21
    {
        public EndingInfoV21[] endings;
    }
}
