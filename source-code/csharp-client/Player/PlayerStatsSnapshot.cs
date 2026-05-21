using System.Text;
using QinghuaStory;
using UnityEngine;

/// <summary>活动请求前截取的玩家属性，用于与活动结果对比生成「变化」文案。</summary>
public struct PlayerStatsSnapshot
{
    public float gpa;
    public int energy;
    public int health;
    public int research_ability_100;
    public int social_ability_100;
    public int failed_credits;
    public int semester_index;

    public static PlayerStatsSnapshot Capture()
    {
        var pm = PlayerManager.Instance;
        if (pm == null) return default;
        var s = pm.stats;
        return new PlayerStatsSnapshot
        {
            gpa = s.gpa,
            energy = s.energy,
            health = s.health,
            research_ability_100 = s.research_ability_100,
            social_ability_100 = s.social_ability_100,
            failed_credits = s.failed_credits,
            semester_index = s.semester_index
        };
    }

    public string FormatDeltaVsCurrent(PlayerStatsData after)
    {
        if (after == null) return "";
        var sb = new StringBuilder();
        AppendDelta(sb, "精力", energy, after.energy);
        AppendDelta(sb, "健康", health, after.health);
        AppendDelta(sb, "科研能力", research_ability_100, after.research_ability_100, suffix: "/100");
        AppendDelta(sb, "社工能力", social_ability_100, after.social_ability_100, suffix: "/100");
        if (semester_index != 0)
            AppendDeltaFloat(sb, "绩点", gpa, after.gpa);
        AppendDelta(sb, "挂课学分", failed_credits, after.failed_credits);
        return sb.ToString().Trim();
    }

    private static void AppendDelta(StringBuilder sb, string label, int before, int after, string suffix = "")
    {
        int d = after - before;
        if (d == 0) return;
        string sign = d > 0 ? "+" : "";
        sb.AppendLine($"{label} {before}{suffix} → {after}{suffix} ({sign}{d})");
    }

    private static void AppendDeltaFloat(StringBuilder sb, string label, float before, float after)
    {
        float d = after - before;
        if (Mathf.Abs(d) < 0.005f) return;
        string sign = d > 0 ? "+" : "";
        sb.AppendLine($"{label} {before:F2} → {after:F2} ({sign}{d:F2})");
    }
}
