using System;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>
    /// 客户端遮罩 UI 打开时暂停服务端时钟，关闭时配对恢复；支持多层嵌套（引用计数）。
    /// </summary>
    public static class ServerPauseCoordinator
    {
        private static int _depth;

        public static int Depth => _depth;

        /// <summary>打开菜单/地图/对话等模态内容时调用。</summary>
        public static void Acquire(MonoBehaviour coroutineHost = null)
        {
            if (_depth == 0)
            {
                APIManager.EnsureExists();
                var host = ResolveCoroutineHost(coroutineHost);
                if (host != null)
                    host.StartCoroutine(PauseOnceRoutine());
                else
                    Debug.LogWarning("[ServerPauseCoordinator] 无法 Pause：无可用协程宿主（请确保 APIManager 所在物体激活）");
            }
            _depth++;
        }

        /// <summary>关闭一层模态内容时调用（须与 Acquire 成对）。</summary>
        public static void Release(MonoBehaviour coroutineHost = null)
        {
            if (_depth <= 0)
            {
                Debug.LogWarning("[ServerPauseCoordinator] Release 多于 Acquire，已忽略");
                return;
            }
            _depth--;
            if (_depth == 0)
            {
                APIManager.EnsureExists();
                var host = ResolveCoroutineHost(coroutineHost);
                if (host != null)
                    host.StartCoroutine(ResumeOnceRoutine());
                else
                    Debug.LogWarning("[ServerPauseCoordinator] 无法 Resume：无可用协程宿主");
            }
        }

        /// <summary>强制归零并恢复时钟（读档/切场景等异常路径）。</summary>
        public static void ForceResumeAll(MonoBehaviour coroutineHost = null)
        {
            if (_depth == 0) return;
            _depth = 0;
            APIManager.EnsureExists();
            var host = ResolveCoroutineHost(coroutineHost);
            if (host != null)
                host.StartCoroutine(ResumeOnceRoutine());
        }

        /// <summary>非激活物体上不能 StartCoroutine；回退到 APIManager。</summary>
        private static MonoBehaviour ResolveCoroutineHost(MonoBehaviour preferred)
        {
            if (preferred != null && preferred.isActiveAndEnabled)
                return preferred;
            APIManager.EnsureExists();
            if (APIManager.Instance != null && APIManager.Instance.isActiveAndEnabled)
                return APIManager.Instance;
            return PlayerManager.Instance != null && PlayerManager.Instance.isActiveAndEnabled
                ? PlayerManager.Instance
                : null;
        }

        private static System.Collections.IEnumerator PauseOnceRoutine()
        {
            if (APIManager.Instance == null) yield break;
            bool done = false;
            APIManager.Instance.PauseTimeV21(_ => done = true, _ => done = true);
            while (!done) yield return null;
        }

        private static System.Collections.IEnumerator ResumeOnceRoutine()
        {
            if (APIManager.Instance == null) yield break;
            bool done = false;
            APIManager.Instance.ResumeTimeV21(_ => done = true, _ => done = true);
            while (!done) yield return null;
        }
    }
}
