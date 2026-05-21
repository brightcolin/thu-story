using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace QinghuaStory
{
    // ========== 通用 ==========
    [Serializable]
    public class HealthResponse
    {
        public string status;
        public bool api_configured;
    }

    [Serializable]
    public class ErrorResponse
    {
        public string detail;
    }

    [Serializable]
    public class ResetResponse
    {
        public string message;
        public string status;
    }

    // ========== GET /player ==========
    [Serializable]
    public class PlayerStatePayload
    {
        public string player_id;
        public float gpa;
        public int energy;
        public int health;
        public int wisdom;
        public int math_skill;
        public int english_skill;
        public int research_skill;
        public int social_skill;
        public int current_week;
        public string current_phase;
        public int total_days;
    }

    [Serializable]
    public class UnlockState
    {
        public bool lab_access;
        public bool club_joined;
        public bool research_project;
        public bool boyfriend_unlocked;
        public bool mentor_close;
    }

    [Serializable]
    public class ServerGameTime
    {
        public int current_week;
        public string week_name;
        public string current_phase;
        public string phase_name;
        public int total_days;
    }

    /// <summary>可与 JsonUtility 解析的部分（friendships 为动态字典，见解析器）。</summary>
    [Serializable]
    public class PlayerDataResponse
    {
        public PlayerStatePayload player;
        public UnlockState unlocks;
        public ServerGameTime time;
    }

    public static class PlayerResponseParser
    {
        public static Dictionary<string, int> ExtractFriendships(string json)
        {
            var d = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(json)) return d;

            int keyIdx = json.IndexOf("\"friendships\"", StringComparison.Ordinal);
            if (keyIdx < 0) return d;
            int brace = json.IndexOf('{', keyIdx);
            if (brace < 0) return d;

            int depth = 0;
            int i = brace;
            for (; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        i++;
                        break;
                    }
                }
            }

            int innerStart = brace + 1;
            int innerLen = i - 1 - innerStart;
            if (innerLen <= 0) return d;
            string inner = json.Substring(innerStart, innerLen);
            var rgx = new Regex("\"([^\"]+)\"\\s*:\\s*(-?\\d+)");
            foreach (Match m in rgx.Matches(inner))
                d[m.Groups[1].Value] = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            return d;
        }
    }

    // ========== POST /chat ==========
    [Serializable]
    public class ChatRequest
    {
        public string npc_id;
        public string message;
    }

    [Serializable]
    public class ChatResponse
    {
        public string npc_name;
        public string reply;
        public string emotion;
        public int friendship_change;
        public int current_friendship;
        public string friendship_tier;
        public string[] newly_unlocked;
    }

    // ========== GET /npcs ==========
    [Serializable]
    public class NPCListResponse
    {
        public NPCInfoItem[] npcs;
    }

    [Serializable]
    public class NPCInfoItem
    {
        public string npc_id;
        public string name;
        public string identity;
        public int friendship;
        public string friendship_tier;
    }

    // ========== GET /activities ==========
    [Serializable]
    public class ActivitiesResponse
    {
        public string current_phase;
        public string phase_name;
        public ActivityListItem[] activities;
    }

    [Serializable]
    public class ActivityListItem
    {
        public string id;
        public string name;
        public string description;
        public string npc_id;
        public EffectPreview effect_preview;
    }

    [Serializable]
    public class EffectPreview
    {
        public int energy;
        public int health;
        public float gpa;
    }

    // ========== POST /activities/execute ==========
    [Serializable]
    public class ActivityExecuteRequest
    {
        public string activity_id;
    }

    [Serializable]
    public class ActivityExecuteResult
    {
        public bool success;
        public string message;
        public string activity_name;
        public string npc_id;
        public EffectApplied effect_applied;
        public PlayerStatePayload new_state;
        public string[] newly_unlocked;
    }

    [Serializable]
    public class EffectApplied
    {
        public int energy;
        public int health;
    }

    // ========== POST /time/advance ==========
    [Serializable]
    public class TimeAdvanceResponse
    {
        public string previous_phase;
        public string current_phase;
        public int current_week;
        public int total_days;
        public bool is_new_day;
    }
}
