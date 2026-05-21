using System;
using System.Collections.Generic;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// 自动排课：周一～周五为 day 0～4；每天 4 节，学时依次为 2、3、2、2（第1～4节，与培养方案一致）。
    /// 规则：每周安排的总学时 = 课程学分；3 学时必须占用第 2 节，2 学时占用第 1/3/4 节。
    /// 在可行的时间组合中随机选取分解方式与空位；若传入已有课表，会避开已被占用的 (day, period)。
    /// </summary>
    public static class CourseScheduleUtil
    {
        /// <summary>period 1～4 对应的单次课学时（索引 0 占位不用）。</summary>
        public static readonly int[] PeriodCreditHours = { 0, 2, 3, 2, 2 };

        public static ScheduleSlotV21[] BuildAutoSlots(int credits) =>
            BuildAutoSlots(credits, null, null);

        /// <param name="courseIdForSpread">非空时参与随机种子，减少不同课程起始格雷同。</param>
        public static ScheduleSlotV21[] BuildAutoSlots(int credits, string courseIdForSpread, ScheduleSlotV21[] existingSchedule)
        {
            int needHours = Mathf.Clamp(credits, 1, 20);
            var occupied = CollectOccupiedCells(existingSchedule);

            int seed = Environment.TickCount ^ UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            if (!string.IsNullOrEmpty(courseIdForSpread))
                seed ^= courseIdForSpread.GetHashCode();

            for (int pass = 0; pass < 64; pass++)
            {
                var rng = new System.Random(seed + pass * 1009);
                if (TryPlaceWeeklyHours(needHours, occupied, rng, out var slots))
                    return slots;
            }

            if (TryDeterministicPlaceWeeklyHours(needHours, occupied, out var fallback))
                return fallback;

            return Array.Empty<ScheduleSlotV21>();
        }

        static bool TryPlaceWeeklyHours(int weeklyHours, HashSet<(int day, int period)> occupied, System.Random rng, out ScheduleSlotV21[] slots)
        {
            slots = null;
            var parts = PickRandomDecomposition(weeklyHours, rng);
            var chunks = new List<int>(parts.threeHourCount + parts.twoHourCount);
            for (int i = 0; i < parts.threeHourCount; i++) chunks.Add(3);
            for (int i = 0; i < parts.twoHourCount; i++) chunks.Add(2);
            Shuffle(chunks, rng);

            var used = new HashSet<(int day, int period)>(occupied);
            var list = new List<ScheduleSlotV21>(chunks.Count);

            foreach (int h in chunks)
            {
                var candidates = CollectCellsWithPeriodHours(h, used);
                if (candidates.Count == 0)
                    return false;
                Shuffle(candidates, rng);
                var cell = candidates[0];
                used.Add(cell);
                list.Add(new ScheduleSlotV21 { day_of_week = cell.day, period = cell.period });
            }

            slots = list.ToArray();
            return true;
        }

        /// <summary>将「每周总学时」拆成若干次 2 学时、3 学时课；若无法用 2/3 凑齐（如 1 学分）则向上取到可表示的最小总学时。</summary>
        static (int threeHourCount, int twoHourCount) PickRandomDecomposition(int weeklyHours, System.Random rng)
        {
            for (int bump = 0; bump < 8; bump++)
            {
                int n = weeklyHours + bump;
                var options = new List<(int t3, int t2)>();
                for (int a = 0; 3 * a <= n; a++)
                {
                    int rem = n - 3 * a;
                    if (rem >= 0 && rem % 2 == 0)
                        options.Add((a, rem / 2));
                }
                if (options.Count > 0)
                    return options[rng.Next(options.Count)];
            }

            return (0, 1);
        }

        static List<(int day, int period)> CollectCellsWithPeriodHours(int hours, HashSet<(int day, int period)> used)
        {
            var list = new List<(int, int)>();
            for (int day = 0; day <= 4; day++)
            {
                for (int p = 1; p <= 4; p++)
                {
                    if (PeriodCreditHours[p] != hours) continue;
                    if (used.Contains((day, p))) continue;
                    list.Add((day, p));
                }
            }
            return list;
        }

        static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>随机多次仍失败时（课表极满），按固定顺序选格，仍满足学时规则。</summary>
        static bool TryDeterministicPlaceWeeklyHours(int weeklyHours, HashSet<(int day, int period)> occupied, out ScheduleSlotV21[] slots)
        {
            slots = null;
            var parts = PickFirstDecomposition(weeklyHours);
            var chunks = new List<int>(parts.threeHourCount + parts.twoHourCount);
            for (int i = 0; i < parts.threeHourCount; i++) chunks.Add(3);
            for (int i = 0; i < parts.twoHourCount; i++) chunks.Add(2);

            var used = new HashSet<(int day, int period)>(occupied);
            var list = new List<ScheduleSlotV21>(chunks.Count);

            foreach (int h in chunks)
            {
                (int day, int period)? found = null;
                for (int day = 0; day <= 4 && found == null; day++)
                {
                    for (int p = 1; p <= 4; p++)
                    {
                        if (PeriodCreditHours[p] != h) continue;
                        if (used.Contains((day, p))) continue;
                        found = (day, p);
                        break;
                    }
                }
                if (found == null)
                    return false;
                used.Add(found.Value);
                list.Add(new ScheduleSlotV21 { day_of_week = found.Value.day, period = found.Value.period });
            }

            slots = list.ToArray();
            return true;
        }

        static (int threeHourCount, int twoHourCount) PickFirstDecomposition(int weeklyHours)
        {
            for (int bump = 0; bump < 8; bump++)
            {
                int n = weeklyHours + bump;
                for (int a = 0; 3 * a <= n; a++)
                {
                    int rem = n - 3 * a;
                    if (rem >= 0 && rem % 2 == 0)
                        return (a, rem / 2);
                }
            }

            return (0, 1);
        }

        static HashSet<(int day, int period)> CollectOccupiedCells(ScheduleSlotV21[] existingSchedule)
        {
            var used = new HashSet<(int, int)>();
            if (existingSchedule == null) return used;
            foreach (var s in existingSchedule)
            {
                if (s == null || string.IsNullOrEmpty(s.course_id)) continue;
                int d = Mathf.Clamp(s.day_of_week, 0, 4);
                int p = Mathf.Clamp(s.period, 1, 4);
                used.Add((d, p));
            }
            return used;
        }
    }
}
