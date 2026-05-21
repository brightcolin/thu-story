using System.Collections;
using UnityEngine;
using QinghuaStory;

/// <summary>
/// 定时 POST /player/penalties/meals（《前端对接说明》v2.2 方案 B）；漏餐判定完全由服务端完成。
/// 活动/上课推进时间后会 RequestImmediatePoll，避免长时间等轮询才扣罚、才弹提示。
/// </summary>
public class MealMissPenaltyMonitor : MonoBehaviour
{
    public static MealMissPenaltyMonitor Instance { get; private set; }

    [SerializeField] [Tooltip("轮询间隔（秒），文档建议如 30")]
    private float pollIntervalSeconds = 30f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        StartCoroutine(PollRoutine());
    }

    /// <summary>在 POST /activities/execute、上课等推进游戏时间后立即检测漏餐。</summary>
    public static void RequestImmediatePoll()
    {
        if (Instance == null) return;
        Instance.StartCoroutine(Instance.PollOnceAfterCurrentFrame());
    }

    /// <summary>错开与活动流程同帧尾巴（SuppressActivityHudToasts 等），再发缺餐请求。</summary>
    private IEnumerator PollOnceAfterCurrentFrame()
    {
        yield return null;
        yield return PollMealPenaltiesOnce();
    }

    private IEnumerator PollRoutine()
    {
        var period = new WaitForSeconds(Mathf.Max(5f, pollIntervalSeconds));
        // 启动后先检一次，避免「开局先空等 30 秒」
        yield return PollMealPenaltiesOnce();
        while (true)
        {
            yield return period;
            yield return PollMealPenaltiesOnce();
        }
    }

    private IEnumerator PollMealPenaltiesOnce()
    {
        if (APIManager.Instance == null || PlayerManager.Instance == null)
            yield break;

        bool done = false;
        string err = null;
        MealPenaltyResponseV21 resp = null;
        APIManager.Instance.PostMealPenaltiesV21(
            r => { resp = r; done = true; },
            e => { err = e; done = true; });
        while (!done) yield return null;

        if (!string.IsNullOrEmpty(err) || resp == null)
            yield break;
        if (!resp.success || !resp.applied)
            yield break;

        PlayerManager.Instance.ProcessMealPenaltyResultV21(resp);
    }
}
