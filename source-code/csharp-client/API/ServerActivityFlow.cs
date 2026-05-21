using System;
using System.Collections;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// POST /activities/execute 后与 ActivityTrigger 相同的后处理：应用 new_state、可选 POST /time/advance、GET /player 刷新。
    /// </summary>
    public static class ServerActivityFlow
    {
        /// <param name="onComplete">result 与 transportError 互斥：网络/解析失败时 result 为 null 且 transportError 非空；业务失败时 result.success==false</param>
        public static IEnumerator Run(string activityId, bool advanceTimeAfterSuccess, Action<ActivityExecuteResult, string> onComplete)
        {
            APIManager.EnsureExists();
            if (APIManager.Instance == null)
            {
                Debug.LogWarning("[ServerActivityFlow] APIManager 缺失");
                onComplete?.Invoke(null, "APIManager 未初始化");
                yield break;
            }

            bool done = false;
            ActivityExecuteResult result = null;
            string err = null;

            APIManager.Instance.DoActivity(activityId,
                r => { result = r; done = true; },
                e => { err = e; done = true; });

            while (!done) yield return null;

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning($"[ServerActivityFlow] {activityId}: {err}");
                onComplete?.Invoke(null, err);
                yield break;
            }

            if (result == null)
            {
                onComplete?.Invoke(null, "空响应");
                yield break;
            }

            if (result.new_state != null && PlayerManager.Instance != null)
                PlayerManager.Instance.ApplyPlayerPayload(result.new_state);

            if (!result.success)
            {
                onComplete?.Invoke(result, null);
                yield break;
            }

            if (advanceTimeAfterSuccess)
            {
                bool tDone = false;
                string tErr = null;
                APIManager.Instance.AdvanceTime(
                    _ => tDone = true,
                    e => { tErr = e; tDone = true; });
                while (!tDone) yield return null;
                if (!string.IsNullOrEmpty(tErr))
                    Debug.LogWarning("[ServerActivityFlow] 推进时间失败: " + tErr);
                else
                    ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
            }

            PlayerManager.Instance?.RefreshFromServer();
            onComplete?.Invoke(result, null);
        }

        /// <summary>v2.1：时间推进由服务端在活动内完成，切勿再 POST /time/advance。</summary>
        /// <param name="timeBeforeExecute">POST execute 前的时刻；食堂 eat_canteen 用于补吃降效与餐段登记。</param>
        public static IEnumerator RunV21(string activityId, string courseId,
            Action<ActivityResultV21, string> onComplete, TimeInfoV21 timeBeforeExecute = null)
        {
            APIManager.EnsureExists();
            if (APIManager.Instance == null)
            {
                onComplete?.Invoke(null, "APIManager 未初始化");
                yield break;
            }

            if (string.IsNullOrEmpty(activityId))
            {
                onComplete?.Invoke(null, "activityId 为空");
                yield break;
            }

            bool done = false;
            ActivityResultV21 result = null;
            string err = null;

            APIManager.Instance.ExecuteActivityV21(activityId, courseId,
                r => { result = r; done = true; },
                e => { err = e; done = true; });

            while (!done) yield return null;

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning("[ServerActivityFlow] v2.1 " + activityId + ": " + err);
                onComplete?.Invoke(null, err);
                yield break;
            }

            if (result == null)
            {
                onComplete?.Invoke(null, "空响应");
                yield break;
            }

            PlayerManager.Instance?.ProcessActivityResultV21(result, activityId, timeBeforeExecute);
            onComplete?.Invoke(result, null);
        }
    }
}
