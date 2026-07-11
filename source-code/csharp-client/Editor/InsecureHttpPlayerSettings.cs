#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 仅在开发者明确选择菜单命令时允许明文 HTTP；生产构建应使用 HTTPS。
/// </summary>
public static class InsecureHttpPlayerSettings
{
    [MenuItem("ThuStory/Enable HTTP for local development")]
    private static void EnableForLocalDevelopment()
    {
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            Debug.Log(
                "[InsecureHttpPlayerSettings] 已允许开发环境使用 HTTP。发布前请切换到 HTTPS 并关闭该选项。");
        }
    }
}
#endif
