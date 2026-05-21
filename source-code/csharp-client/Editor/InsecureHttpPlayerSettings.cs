#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 每次刷新脚本后强制打开「允许 HTTP」，避免工程 YAML 与编辑器不同步导致 UnityWebRequest 仍拦截。
/// </summary>
[InitializeOnLoad]
public static class InsecureHttpPlayerSettings
{
    static InsecureHttpPlayerSettings()
    {
        if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            Debug.Log(
                "[InsecureHttpPlayerSettings] 已设置 PlayerSettings：Allow downloads over HTTP = Always allowed。");
        }
    }
}
#endif
