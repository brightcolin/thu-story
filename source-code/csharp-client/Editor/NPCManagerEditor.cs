using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NPCManager))]
public class NPCManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var mgr = (NPCManager)target;
        EditorGUI.BeginDisabledGroup(!Application.isPlaying || mgr._chatCanvas == null);
        EditorGUILayout.Space(4);
        if (GUILayout.Button("▶ 应用布局 (Apply Layout)", GUILayout.Height(24)))
        {
            mgr.ApplyLayoutNow();
        }
        EditorGUI.EndDisabledGroup();
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("需在运行模式下打开对话后，修改参数并点击上方按钮或切换选中刷新。", MessageType.Info);
    }
}
