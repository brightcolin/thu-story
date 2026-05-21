using System;
using System.Collections.Generic;

namespace QinghuaStory
{
    /// <summary>
    /// Unity JsonUtility 对部分接口返回的 orgs 数组解析不稳定时，用手动切分每个组织对象再 FromJson。
    /// </summary>
    public static class SocialOrgsJsonUtil
    {
        public static SocialOrgInfoV21[] ExtractOrgs(string json)
        {
            if (string.IsNullOrEmpty(json))
                return Array.Empty<SocialOrgInfoV21>();

            int orgsKey = json.IndexOf("\"orgs\"", StringComparison.Ordinal);
            if (orgsKey < 0)
                return Array.Empty<SocialOrgInfoV21>();

            int lb = json.IndexOf('[', orgsKey);
            if (lb < 0)
                return Array.Empty<SocialOrgInfoV21>();

            var list = new List<SocialOrgInfoV21>();
            int i = lb + 1;
            while (i < json.Length)
            {
                while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == ','))
                    i++;
                if (i >= json.Length || json[i] == ']')
                    break;
                if (json[i] != '{')
                {
                    i++;
                    continue;
                }

                int end = FindMatchingClosingBrace(json, i);
                if (end < 0)
                    break;

                string slice = json.Substring(i, end - i + 1);
                try
                {
                    var item = UnityEngine.JsonUtility.FromJson<SocialOrgInfoV21>(slice);
                    if (item != null && !string.IsNullOrEmpty(item.id))
                        list.Add(item);
                }
                catch
                {
                    /* 跳过损坏片段 */
                }

                i = end + 1;
            }

            return list.ToArray();
        }

        static int FindMatchingClosingBrace(string s, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"')
                {
                    i++;
                    while (i < s.Length)
                    {
                        if (s[i] == '\\')
                        {
                            i += 2;
                            if (i > s.Length) break;
                            continue;
                        }
                        if (s[i] == '"')
                            break;
                        i++;
                    }
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }
    }
}
