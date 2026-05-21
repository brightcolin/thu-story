using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using QinghuaStory;

/// <summary>
/// 时间重置：POST /save/reset 写入后端全量初始档 → POST /time/resume（重置后时钟默认暂停）→
/// 清好感缓存 → GET /player 刷新所有 HUD 数值与时间显示。
/// </summary>
public class GameTimeRestart : MonoBehaviour
{
    private void Start()
    {
        var btn = GetComponent<Button>() ?? GetComponentInParent<Button>();
        if (btn != null)
            btn.onClick.AddListener(RestartTime);
        else
            Debug.LogWarning("GameTimeRestart: 未找到 Button 组件，请将此脚本挂在 Button 或其子物体上。");
    }

    /// <summary>供 Button OnClick 或代码调用。</summary>
    public void RestartTime()
    {
        APIManager.EnsureExists();

        if (APIManager.Instance == null)
        {
            Debug.LogWarning("GameTimeRestart: 无 APIManager，仅本地重置（不会写入后端）。");
            LocalFallbackRestart();
            return;
        }

        StartCoroutine(FullRestartRoutine());
    }

    private IEnumerator FullRestartRoutine()
    {
        bool resetDone = false;
        bool resetOk = false;
        APIManager.Instance.ResetGame(
            _ => { resetOk = true; resetDone = true; },
            err =>
            {
                Debug.LogWarning("GameTimeRestart: POST /save/reset 失败: " + err);
                resetDone = true;
            });
        while (!resetDone) yield return null;

        if (!resetOk)
            yield break;

        bool resumeDone = false;
        APIManager.Instance.ResumeTimeV21(
            _ => resumeDone = true,
            err =>
            {
                Debug.LogWarning("GameTimeRestart: POST /time/resume 失败: " + err);
                resumeDone = true;
            });
        while (!resumeDone) yield return null;

        var fp = Object.FindObjectOfType<FriendshipPersistence>(true);
        if (fp != null)
            fp.ClearSavedData();
        else
            PlayerManager.Instance?.ClearFriendships();

        if (PlayerManager.Instance != null)
        {
            bool fetchDone = false;
            PlayerManager.Instance.RefreshFromServer(() => fetchDone = true);
            while (!fetchDone) yield return null;
        }

        // Restart 不重载场景：CourseSelectionUI.Start 不会第二次跑，且 semester_index 仍为 0 时无学期切换事件
        var courseUi = Object.FindObjectOfType<CourseSelectionUI>(true);
        if (courseUi != null)
            courseUi.AfterFullGameRestart();

        Debug.Log("GameTimeRestart: 回档完成，数值与时间已与后端初始档同步");
    }

    private static void LocalFallbackRestart()
    {
        Debug.LogWarning("GameTimeRestart: 无后端连接，仅清除本地好感缓存（游戏时间以服务端为准）。");

        var fp = Object.FindObjectOfType<FriendshipPersistence>(true);
        if (fp != null)
            fp.ClearSavedData();
        else
            PlayerManager.Instance?.ClearFriendships();
    }
}
