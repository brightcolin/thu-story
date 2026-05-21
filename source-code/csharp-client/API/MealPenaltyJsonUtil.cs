using System;
using System.Collections.Generic;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// Unity JsonUtility 对嵌套数组、缺省字段解析不可靠，对 /player/penalties/meals 响应做补全。
    /// </summary>
    public static class MealPenaltyJsonUtil
    {
        public static void SupplementFromRawJson(string json, MealPenaltyResponseV21 r)
        {
            if (r == null || string.IsNullOrEmpty(json)) return;

            // JsonUtility 对缺省 bool 为 false；省略 success 且 applied:true 时补 true，但不覆盖显式 success:false
            if (!r.success && r.applied &&
                json.IndexOf("\"success\":false", StringComparison.OrdinalIgnoreCase) < 0 &&
                json.IndexOf("\"success\": false", StringComparison.OrdinalIgnoreCase) < 0)
                r.success = true;

            if (r.applied && (r.missed_meals == null || r.missed_meals.Length == 0))
            {
                var extracted = ExtractMissedMeals(json);
                if (extracted.Length > 0)
                    r.missed_meals = extracted;
            }

            // snake / camel 增量字段补全（解析失败时为 0）
            if (r.applied && r.energy_delta == 0 && r.health_delta == 0)
            {
                if (TryReadIntAfterKey(json, "energy_delta", out int ed) ||
                    TryReadIntAfterKey(json, "energyDelta", out ed))
                    r.energy_delta = ed;
                if (TryReadIntAfterKey(json, "health_delta", out int hd) ||
                    TryReadIntAfterKey(json, "healthDelta", out hd))
                    r.health_delta = hd;
            }
        }

        public static string[] ExtractMissedMeals(string json)
        {
            if (string.IsNullOrEmpty(json)) return Array.Empty<string>();

            int keyIdx = json.IndexOf("\"missed_meals\"", StringComparison.Ordinal);
            if (keyIdx < 0)
                keyIdx = json.IndexOf("\"missedMeals\"", StringComparison.Ordinal);
            if (keyIdx < 0)
                return Array.Empty<string>();

            int lb = json.IndexOf('[', keyIdx);
            if (lb < 0) return Array.Empty<string>();

            var list = new List<string>();
            int i = lb + 1;
            while (i < json.Length)
            {
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i >= json.Length || json[i] == ']') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"')
                {
                    i++;
                    continue;
                }

                i++;
                int start = i;
                while (i < json.Length)
                {
                    if (json[i] == '\\') { i += 2; continue; }
                    if (json[i] == '"') break;
                    i++;
                }
                if (i > start)
                    list.Add(json.Substring(start, i - start));
                i++;
            }

            return list.ToArray();
        }

        private static bool TryReadIntAfterKey(string json, string key, out int value)
        {
            value = 0;
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return false;
            int colon = json.IndexOf(':', k);
            if (colon < 0 || colon > k + key.Length + 12) return false;
            int j = colon + 1;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            int sign = 1;
            if (j < json.Length && json[j] == '-') { sign = -1; j++; }
            if (j >= json.Length || !char.IsDigit(json[j])) return false;
            int num = 0;
            while (j < json.Length && char.IsDigit(json[j]))
            {
                num = num * 10 + (json[j] - '0');
                j++;
            }
            value = sign * num;
            return true;
        }
    }
}
