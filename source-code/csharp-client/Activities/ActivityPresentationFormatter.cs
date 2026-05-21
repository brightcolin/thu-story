using System.Collections.Generic;
using System.Text;
using QinghuaStory;

/// <summary>活动结果底部说明文案：属性变化、时间、解锁与事件。</summary>
public static class ActivityPresentationFormatter
{
    public static string FormatV21(ActivityResultV21 r, PlayerStatsSnapshot before)
    {
        if (r == null) return "";
        var sb = new StringBuilder();

        if (r.new_state != null)
        {
            var ns = r.new_state;
            AppendDelta(sb, "精力", before.energy, ns.energy);
            AppendDelta(sb, "健康", before.health, ns.health);
            AppendDelta(sb, "科研能力", before.research_ability_100, ns.research_ability);
            AppendDelta(sb, "社工能力", before.social_ability_100, ns.social_ability);
            if (before.semester_index != 0)
                AppendDeltaFloat(sb, "绩点", before.gpa, ns.gpa);
            AppendDelta(sb, "挂课学分", before.failed_credits, ns.failed_credits);
        }
        else
        {
            var pm = PlayerManager.Instance;
            if (pm != null)
            {
                string rest = before.FormatDeltaVsCurrent(pm.stats);
                if (!string.IsNullOrEmpty(rest))
                    sb.Append(rest);
            }
        }

        if (r.time_advanced_minutes != 0)
            sb.AppendLine($"游戏内时间推进 {r.time_advanced_minutes} 分钟");
        if (r.new_time != null)
            sb.AppendLine($"当前时间　{r.new_time.date_display}　{r.new_time.time_display}");

        if (r.newly_unlocked != null && r.newly_unlocked.Length > 0)
        {
            sb.AppendLine("解锁：");
            foreach (var u in r.newly_unlocked)
                if (!string.IsNullOrEmpty(u))
                    sb.AppendLine($"· {u}");
        }

        if (r.events != null && r.events.Length > 0)
        {
            foreach (var ev in r.events)
            {
                if (ev == null) continue;
                string msg = string.IsNullOrEmpty(ev.message) ? ev.type : ev.message;
                if (!string.IsNullOrEmpty(msg))
                    sb.AppendLine(msg);
            }
        }

        if (!string.IsNullOrEmpty(r.course_id))
        {
            string name = string.IsNullOrEmpty(r.course_name) ? r.course_id : r.course_name;
            sb.AppendLine($"· {name}　掌握 {(r.mastery_delta >= 0 ? "+" : "")}{r.mastery_delta}");
        }

        string s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "已完成该活动。" : s;
    }

    public static string FormatLegacy(ActivityExecuteResult r, PlayerStatsSnapshot before)
    {
        if (r == null) return "";
        var sb = new StringBuilder();

        if (r.effect_applied != null)
        {
            if (r.effect_applied.energy != 0)
                sb.AppendLine($"精力变化　{(r.effect_applied.energy > 0 ? "+" : "")}{r.effect_applied.energy}");
            if (r.effect_applied.health != 0)
                sb.AppendLine($"健康变化　{(r.effect_applied.health > 0 ? "+" : "")}{r.effect_applied.health}");
        }

        if (r.new_state != null)
        {
            AppendDelta(sb, "精力", before.energy, r.new_state.energy);
            AppendDelta(sb, "健康", before.health, r.new_state.health);
            if (before.semester_index != 0)
                AppendDeltaFloat(sb, "绩点", before.gpa, r.new_state.gpa);
        }
        else
        {
            var pm = PlayerManager.Instance;
            if (pm != null)
            {
                string rest = before.FormatDeltaVsCurrent(pm.stats);
                if (!string.IsNullOrEmpty(rest))
                    sb.Append(rest);
            }
        }

        if (r.newly_unlocked != null && r.newly_unlocked.Length > 0)
        {
            sb.AppendLine("解锁：");
            foreach (var u in r.newly_unlocked)
                if (!string.IsNullOrEmpty(u))
                    sb.AppendLine($"· {u}");
        }

        string s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "已完成该活动。" : s;
    }

    public static string FormatAttendBatch(IReadOnlyList<AttendClassResponseV21> batch)
    {
        if (batch == null || batch.Count == 0) return "本节未有上课记录。";
        var sb = new StringBuilder();
        foreach (var r in batch)
        {
            if (r == null) continue;
            string statusCn = (r.attendance_status ?? "") switch
            {
                "on_time" => "出勤",
                "late" => "迟到",
                "absent" => "缺勤",
                _ => string.IsNullOrEmpty(r.attendance_status) ? "—" : r.attendance_status
            };
            string name = string.IsNullOrEmpty(r.course_name) ? "课程" : r.course_name;
            if (r.success)
                sb.AppendLine($"· {name}　{statusCn}　掌握 {(r.mastery_delta >= 0 ? "+" : "")}{r.mastery_delta}");
            else
                sb.AppendLine($"· {name}　未完成");
        }
        var pm = PlayerManager.Instance;
        if (pm != null)
        {
            string hint = "属性已根据服务器结果更新，详见顶部状态栏。";
            sb.AppendLine(hint);
        }
        return sb.ToString().Trim();
    }

    private static void AppendDelta(StringBuilder sb, string label, int before, int after)
    {
        int d = after - before;
        if (d == 0) return;
        string sign = d > 0 ? "+" : "";
        sb.AppendLine($"{label}　{before} → {after}　({sign}{d})");
    }

    private static void AppendDeltaFloat(StringBuilder sb, string label, float before, float after)
    {
        float d = after - before;
        if (UnityEngine.Mathf.Abs(d) < 0.005f) return;
        string sign = d > 0 ? "+" : "";
        sb.AppendLine($"{label}　{before:F2} → {after:F2}　({sign}{d:F2})");
    }
}
