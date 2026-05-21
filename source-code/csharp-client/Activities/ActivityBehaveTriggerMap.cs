using System;

/// <summary>服务端 activity id → behave Animator 触发器名（与 New Animator Controller 一致）。</summary>
public static class ActivityBehaveTriggerMap
{
    public static string Resolve(string activityId, bool librarySelfStudy = false)
    {
        if (librarySelfStudy) return "read";
        if (string.IsNullOrEmpty(activityId)) return null;
        if (activityId.StartsWith("study_library_", StringComparison.Ordinal)) return "read";
        if (activityId == "eat_canteen" || activityId.Contains("eat")) return "eat";
        if (activityId == "exercise" || activityId.Contains("exercise")) return "run";
        if (activityId.Contains("chat_roommate")) return "game";
        if (activityId.Contains("research") || activityId.Contains("consult") || activityId.Contains("lab_use"))
            return "lab";
        if (activityId.Contains("sleep") || activityId == "rest") return "sleep";
        if (activityId == "attend_class") return "class";
        return null;
    }
}
