using System.Collections.Generic;
using System.Text;
using UnityEngine;
using QinghuaStory;

/// <summary>
/// 每个可玩日块开始时的属性与好感快照，用于「每日总结」第一页的当日净变化。
/// 首次拉取 /player 后由 <see cref="PlayerManager"/> 调用 <see cref="EnsureInitialized"/>；
/// 总结关闭并跳到次日 6:30 后调用 <see cref="CaptureFrom"/> 重置基线。
/// </summary>
public static class DailyProgressBaseline
{
    static string NormalizeNpcId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        return id.Trim().ToLowerInvariant();
    }

    static string NormalizeCourseId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        return id.Trim().ToLowerInvariant();
    }

    /// <summary>合并同义 normalized key（大小写/空白差异）的好感，取最后一次出现的值（通常相同）。</summary>
    static void MergeFriendInto(Dictionary<string, int> target, string rawId, int value)
    {
        string n = NormalizeNpcId(rawId);
        if (string.IsNullOrEmpty(n)) return;
        target[n] = value;
    }

    private struct StatBlock
    {
        public float gpa;
        public int energy;
        public int health;
        public int wisdom;
        public int math_skill;
        public int english_skill;
        public int research_skill;
        public int social_skill;
        public int research_ability_100;
        public int social_ability_100;
        public int failed_credits;
        public int semester_index;

        public static StatBlock From(PlayerStatsData s)
        {
            if (s == null) return default;
            return new StatBlock
            {
                gpa = s.gpa,
                energy = s.energy,
                health = s.health,
                wisdom = s.wisdom,
                math_skill = s.math_skill,
                english_skill = s.english_skill,
                research_skill = s.research_skill,
                social_skill = s.social_skill,
                research_ability_100 = s.research_ability_100,
                social_ability_100 = s.social_ability_100,
                failed_credits = s.failed_credits,
                semester_index = s.semester_index
            };
        }
    }

    private static bool _initialized;
    private static StatBlock _baselineStats;
    private static readonly Dictionary<string, int> _baselineFriends = new();
    private static readonly Dictionary<string, float> _baselineCourseMastery = new();

    public static void EnsureInitialized(PlayerManager pm)
    {
        if (_initialized || pm == null) return;
        CaptureFrom(pm, MyCoursesSnapshotCache.LastResponse);
        _initialized = true;
    }

    /// <summary>将当前玩家状态记为「今日起点」（通常在总结关闭、时间已到次日 6:30 并刷新 /player 之后）。</summary>
    /// <param name="coursesMineOrNull">当日的「我的课程」快照；为空时用 <see cref="MyCoursesSnapshotCache.LastResponse"/>。</param>
    public static void CaptureFrom(PlayerManager pm, MyCoursesResponseV21 coursesMineOrNull = null)
    {
        if (pm?.stats == null) return;
        _baselineStats = StatBlock.From(pm.stats);
        _baselineFriends.Clear();
        foreach (var kv in pm.GetFriendshipsSnapshot())
            MergeFriendInto(_baselineFriends, kv.Key, kv.Value);

        // 仅当拿到非空课程列表时才重写掌握度基线；否则保留旧基线，避免拉取失败导致次日全部「首次记入」
        var mine = coursesMineOrNull ?? MyCoursesSnapshotCache.LastResponse;
        if (mine?.courses != null && mine.courses.Length > 0)
        {
            _baselineCourseMastery.Clear();
            foreach (var c in mine.courses)
            {
                if (c == null || string.IsNullOrEmpty(c.course_id)) continue;
                string cid = NormalizeCourseId(c.course_id);
                if (string.IsNullOrEmpty(cid)) continue;
                _baselineCourseMastery[cid] = c.mastery;
            }
        }
    }

    /// <summary>供调试/读档复位：清除已初始化标记，下次拉 player 会重新抓拍。</summary>
    public static void ResetInitializationFlag()
    {
        _initialized = false;
    }

    public static string BuildDeltaSummary(PlayerManager pm, MyCoursesResponseV21 currentMineOrNull = null)
    {
        if (pm?.stats == null) return "（无玩家数据）";
        var s = pm.stats;
        var statsPart = new StringBuilder();
        var b = _baselineStats;

        AppendIntDelta(statsPart, "精力", b.energy, s.energy);
        AppendIntDelta(statsPart, "健康", b.health, s.health);
        AppendIntDelta(statsPart, "智慧", b.wisdom, s.wisdom);
        AppendIntDelta(statsPart, "数学", b.math_skill, s.math_skill);
        AppendIntDelta(statsPart, "英语", b.english_skill, s.english_skill);
        AppendIntDelta(statsPart, "科研", b.research_skill, s.research_skill);
        AppendIntDelta(statsPart, "社工", b.social_skill, s.social_skill);
        AppendIntDelta(statsPart, "科研能力", b.research_ability_100, s.research_ability_100, suffix: "/100");
        AppendIntDelta(statsPart, "社工能力", b.social_ability_100, s.social_ability_100, suffix: "/100");
        if (b.semester_index != 0)
            AppendFloatDelta(statsPart, "绩点", b.gpa, s.gpa);
        AppendIntDelta(statsPart, "挂课学分", b.failed_credits, s.failed_credits);

        var sb = new StringBuilder();
        if (statsPart.Length == 0)
            sb.AppendLine("今日属性无净变化");
        else
            sb.Append(statsPart);

        sb.AppendLine();
        sb.AppendLine("— 好感度 —");
        bool anyFriend = false;
        var nowByNorm = new Dictionary<string, int>();
        foreach (var kv in pm.GetFriendshipsSnapshot())
            MergeFriendInto(nowByNorm, kv.Key, kv.Value);

        var seen = new HashSet<string>();
        foreach (var k in _baselineFriends.Keys) seen.Add(k);
        foreach (var k in nowByNorm.Keys) seen.Add(k);

        foreach (string idNorm in seen)
        {
            _baselineFriends.TryGetValue(idNorm, out int before);
            nowByNorm.TryGetValue(idNorm, out int after);
            int d = after - before;
            if (d == 0) continue;
            anyFriend = true;
            string name = NPCManager.GetNpcDisplayName(idNorm);
            string sign = d > 0 ? "+" : "";
            sb.AppendLine($"{name}  {before} → {after}  ({sign}{d})");
        }

        if (!anyFriend)
            sb.AppendLine("今日无变化");

        var mineNow = currentMineOrNull ?? MyCoursesSnapshotCache.LastResponse;
        sb.AppendLine();
        sb.AppendLine("— 课程掌握度 —");
        AppendCourseMasteryDeltas(sb, mineNow);

        return sb.ToString().TrimEnd();
    }

    private static void AppendCourseMasteryDeltas(StringBuilder sb, MyCoursesResponseV21 mineNow)
    {
        if (mineNow?.courses == null || mineNow.courses.Length == 0)
        {
            if (_baselineCourseMastery.Count == 0)
                sb.AppendLine("（暂无课程数据；上课或打开「我的课程」后，总结将显示掌握度变化）");
            else
                sb.AppendLine("（未能获取今日课程列表；掌握度变化可能不完整）");
            return;
        }

        bool any = false;
        foreach (var c in mineNow.courses)
        {
            if (c == null || string.IsNullOrEmpty(c.course_id)) continue;
            string cid = NormalizeCourseId(c.course_id);
            if (string.IsNullOrEmpty(cid)) continue;
            string name = string.IsNullOrEmpty(c.course_name) ? c.course_id : c.course_name;
            float after = c.mastery;
            bool hadBefore = _baselineCourseMastery.TryGetValue(cid, out float before);
            if (!hadBefore)
            {
                sb.AppendLine($"{name}　掌握 {after:F0}（本日首次记入）");
                any = true;
                continue;
            }

            float d = after - before;
            if (Mathf.Abs(d) < 0.05f) continue;

            any = true;
            int bi = Mathf.RoundToInt(before);
            int ai = Mathf.RoundToInt(after);
            int di = Mathf.RoundToInt(d);
            if (di >= 0)
                sb.AppendLine($"{name}　掌握 {bi}+{di}={ai}");
            else
                sb.AppendLine($"{name}　掌握 {bi}{di}={ai}");
        }

        if (!any)
            sb.AppendLine("今日各课掌握度无变化");
    }

    private static void AppendIntDelta(StringBuilder sb, string label, int before, int after, string suffix = "")
    {
        int d = after - before;
        if (d == 0) return;
        string sign = d > 0 ? "+" : "";
        sb.AppendLine($"{label}  {before}{suffix} → {after}{suffix}  ({sign}{d})");
    }

    private static void AppendFloatDelta(StringBuilder sb, string label, float before, float after)
    {
        float d = after - before;
        if (Mathf.Abs(d) < 0.005f) return;
        string sign = d > 0 ? "+" : "";
        sb.AppendLine($"{label}  {before:F2} → {after:F2}  ({sign}{d:F2})");
    }
}
