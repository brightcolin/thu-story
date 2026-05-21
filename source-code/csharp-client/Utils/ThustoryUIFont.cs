using TMPro;
using UnityEngine;

/// <summary>
/// 全项目 TMP 中文显示：与 TextMesh Pro → Resources → TMP Settings 的默认字体一致（思源黑体 SDF）。
/// 运行时创建的 TextMeshProUGUI 若未指定 font，会自动用 TMP 默认；此处供显式赋值或兜底。
/// </summary>
public static class ThustoryUIFont
{
    public static TMP_FontAsset GetDefaultCjkFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;
        return null;
    }

    public static void Apply(TMP_Text tmp)
    {
        if (tmp == null) return;
        var f = GetDefaultCjkFont();
        if (f == null) return;
        tmp.font = f;
        if (f.material != null)
            tmp.fontSharedMaterial = f.material;
    }
}
