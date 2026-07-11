using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace QinghuaStory
{
    /// <summary>统一后端通信入口。可在场景中显式挂载，否则 PlayerManager 会创建。</summary>
    public class APIManager : MonoBehaviour
    {
        public const string DefaultApiBase = "http://127.0.0.1:8000";
        public const string DefaultApiToken = "local-dev-token-change-me";

        [Header("后端")]
        [SerializeField] private string apiBase = DefaultApiBase;
        [SerializeField] private string apiToken = DefaultApiToken;

        [Header("v2.1 时间")]
        [Tooltip("后端存档重置后时钟默认不推进，需 POST /time/resume 后才会走表。启动时自动 Resume 一次。")]
        [SerializeField] private bool resumeServerClockOnStart = true;

        public static APIManager Instance { get; private set; }

        public static string ApiBase
        {
            get
            {
                string fromEnvironment = Environment.GetEnvironmentVariable("THUSTORY_API_BASE");
                string configured = !string.IsNullOrWhiteSpace(fromEnvironment)
                    ? fromEnvironment
                    : Instance != null ? Instance.apiBase : DefaultApiBase;
                return configured.TrimEnd('/');
            }
        }

        public static string ApiToken
        {
            get
            {
                string fromEnvironment = Environment.GetEnvironmentVariable("THUSTORY_API_TOKEN");
                return !string.IsNullOrWhiteSpace(fromEnvironment)
                    ? fromEnvironment
                    : Instance != null ? Instance.apiToken : DefaultApiToken;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (resumeServerClockOnStart)
                StartCoroutine(BootResumeServerClockRoutine());
            EnsureBackendToolkit();
        }

        /// <summary>无场景配置时自动挂接游戏菜单（课表/社工/结局等）与上课提醒。</summary>
        private static void EnsureBackendToolkit()
        {
            if (UnityEngine.Object.FindObjectOfType<BackendGameplayMenu>() == null)
            {
                var go = new GameObject("BackendGameplayMenu");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<BackendGameplayMenu>();
            }
            if (UnityEngine.Object.FindObjectOfType<ClassPeriodNotifier>() == null)
            {
                var go2 = new GameObject("ClassPeriodNotifier");
                UnityEngine.Object.DontDestroyOnLoad(go2);
                go2.AddComponent<ClassPeriodNotifier>();
            }
            if (UnityEngine.Object.FindObjectOfType<ClassPeriodAutoAbsenceMonitor>() == null)
            {
                var go3 = new GameObject("ClassPeriodAutoAbsenceMonitor");
                UnityEngine.Object.DontDestroyOnLoad(go3);
                go3.AddComponent<ClassPeriodAutoAbsenceMonitor>();
            }
            if (UnityEngine.Object.FindObjectOfType<DayEndSummaryMonitor>() == null)
            {
                var go4 = new GameObject("DayEndSummaryMonitor");
                UnityEngine.Object.DontDestroyOnLoad(go4);
                go4.AddComponent<DayEndSummaryMonitor>();
            }
            if (UnityEngine.Object.FindObjectOfType<MealMissPenaltyMonitor>() == null)
            {
                var go5 = new GameObject("MealMissPenaltyMonitor");
                UnityEngine.Object.DontDestroyOnLoad(go5);
                go5.AddComponent<MealMissPenaltyMonitor>();
            }
        }

        /// <summary>
        /// 后端在 /save/reset 或初始状态下，游戏分钟数不会增长，直到调用 /time/resume。
        /// </summary>
        private IEnumerator BootResumeServerClockRoutine()
        {
            bool done = false;
            PauseResumeResponseV21 resp = null;
            string err = null;
            ResumeTimeV21(
                r => { resp = r; done = true; },
                e => { err = e; done = true; });
            while (!done) yield return null;

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning("[APIManager] 启动时恢复后端时钟失败（/time/resume）: " + err);
                yield break;
            }

            if (resp != null && resp.time != null)
                Debug.Log($"[APIManager] 后端时钟已恢复，当前 {resp.time.time_display}（paused={resp.paused}）");
        }

        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("APIManager");
            go.AddComponent<APIManager>();
        }

        private IEnumerator GetRequest<T>(string endpoint, Action<T> onSuccess, Action<string> onError)
        {
            string url = ApiBase + endpoint;
            string body = null;
            string terr = null;
            yield return ApiTransport.SendGet(url, ApiToken, (b, e) => { body = b; terr = e; });
            HandleTextResponse(body, terr, onSuccess, onError);
        }

        private static string TrimJsonPreamble(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = text.Trim();
            if (text.Length > 0 && text[0] == '\uFEFF')
                text = text.Substring(1);
            return text.TrimStart();
        }

        /// <summary>Unity JsonUtility 不能直接解析根为数组的 JSON，包一层对象。</summary>
        private static string WrapRootJsonArray(string text, string arrayPropertyName)
        {
            text = TrimJsonPreamble(text);
            if (string.IsNullOrEmpty(text) || text[0] != '[')
                return text;
            return "{\"" + arrayPropertyName + "\":" + text + "}";
        }

        private IEnumerator PostRequest<T>(string endpoint, object data, Action<T> onSuccess, Action<string> onError)
        {
            string url = ApiBase + endpoint;
            string jsonData = JsonUtility.ToJson(data);
            string body = null;
            string terr = null;
            yield return ApiTransport.SendPostJson(url, ApiToken, jsonData, (b, e) => { body = b; terr = e; });
            HandleTextResponse(body, terr, onSuccess, onError);
        }

        private IEnumerator PostRequestNoBody<T>(string endpoint, Action<T> onSuccess, Action<string> onError)
        {
            string url = ApiBase + endpoint;
            string body = null;
            string terr = null;
            yield return ApiTransport.SendPostEmpty(url, ApiToken, (b, e) => { body = b; terr = e; });
            HandleTextResponse(body, terr, onSuccess, onError);
        }

        private IEnumerator PatchRequest<T>(string endpoint, string jsonBody, Action<T> onSuccess, Action<string> onError)
        {
            // v2.1: 用于调试 PATCH /player 等
            string url = ApiBase + endpoint;
            string body = null;
            string terr = null;
            yield return ApiTransport.SendPatchJson(url, ApiToken, jsonBody ?? "{}", (b, e) => { body = b; terr = e; });
            HandleTextResponse(body, terr, onSuccess, onError);
        }

        private static void HandleTextResponse<T>(string text, string transportError, Action<T> onSuccess, Action<string> onError)
        {
            if (!string.IsNullOrEmpty(transportError))
            {
                onError?.Invoke(transportError);
                return;
            }

            text = TrimJsonPreamble(text);
            if (string.IsNullOrEmpty(text))
            {
                onError?.Invoke("空响应");
                return;
            }

            try
            {
                T response = JsonUtility.FromJson<T>(text);
                onSuccess?.Invoke(response);
            }
            catch (Exception e)
            {
                int n = Mathf.Min(text.Length, 160);
                string preview = text.Substring(0, n).Replace('\r', ' ').Replace('\n', ' ');
                if (text.Length > n) preview += "…";
                onError?.Invoke("JSON解析失败: " + e.Message + " | 响应片段: " + preview);
            }
        }

        public void CheckHealth(Action<HealthResponse> onSuccess, Action<string> onError) =>
            StartCoroutine(GetRequest("/health", onSuccess, onError));

        public void GetPlayerState(Action<PlayerDataResponse> onSuccess, Action<string> onError) =>
            StartCoroutine(GetRequest("/player", onSuccess, onError));

        public void GetNPCList(Action<NPCListResponse> onSuccess, Action<string> onError) =>
            StartCoroutine(GetRequest("/npcs", onSuccess, onError));

        public void ChatWithNPC(string npcId, string message, Action<ChatResponse> onSuccess, Action<string> onError)
        {
            var data = new ChatRequest { npc_id = npcId, message = message };
            StartCoroutine(PostRequest("/chat", data, onSuccess, onError));
        }

        public void GetActivities(Action<ActivitiesResponse> onSuccess, Action<string> onError) =>
            StartCoroutine(GetRequest("/activities", onSuccess, onError));

        public void DoActivity(string activityId, Action<ActivityExecuteResult> onSuccess, Action<string> onError)
        {
            var data = new ActivityExecuteRequest { activity_id = activityId };
            StartCoroutine(PostRequest("/activities/execute", data, onSuccess, onError));
        }

        public void GetTime(Action<ServerGameTime> onSuccess, Action<string> onError) =>
            StartCoroutine(GetRequest("/time", onSuccess, onError));

        public void AdvanceTime(Action<TimeAdvanceResponse> onSuccess, Action<string> onError) =>
            StartCoroutine(PostRequestNoBody("/time/advance", onSuccess, onError));

        // =========================================================
        // v2.1 新接口（不影响旧版调用方）
        // =========================================================

        public void GetTimeV21(Action<TimeInfoV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/time", onSuccess, onError));

        public void PauseTimeV21(Action<PauseResumeResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PostRequestNoBody("/time/pause", onSuccess, onError));

        public void ResumeTimeV21(Action<PauseResumeResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PostRequestNoBody("/time/resume", onSuccess, onError));

        public void NextDayV21(Action<NextDayResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PostRequestNoBody("/time/nextday", onSuccess, onError));

        public void GetPlayerV21(Action<PlayerFullResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/player", onSuccess, onError));

        /// <summary>PATCH /player。《前端对接指南 v2.1》§12：按请求体<strong>设置</strong>对应字段；本客户端对 energy/health 发<strong>绝对值</strong>（非增量）。若后端按增量实现需改发差值。</summary>
        public void PatchPlayerV21(string jsonBody, Action<PlayerFullResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PatchRequest("/player", jsonBody, onSuccess, onError));

        /// <summary>POST /player/penalties/meals（空 body）；服务端按当前时间判定缺餐并扣罚（v2.2）。</summary>
        public void PostMealPenaltiesV21(Action<MealPenaltyResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PostMealPenaltiesV21Core(onSuccess, onError));

        private IEnumerator PostMealPenaltiesV21Core(Action<MealPenaltyResponseV21> onSuccess, Action<string> onError)
        {
            string url = ApiBase + "/player/penalties/meals";
            string body = null;
            string terr = null;
            yield return ApiTransport.SendPostEmpty(url, ApiToken, (b, e) => { body = b; terr = e; });

            if (!string.IsNullOrEmpty(terr))
            {
                onError?.Invoke(terr);
                yield break;
            }

            body = TrimJsonPreamble(body ?? "");
            if (string.IsNullOrEmpty(body))
            {
                onError?.Invoke("空响应");
                yield break;
            }

            try
            {
                var r = JsonUtility.FromJson<MealPenaltyResponseV21>(body);
                MealPenaltyJsonUtil.SupplementFromRawJson(body, r);
                onSuccess?.Invoke(r);
            }
            catch (Exception e)
            {
                int n = Mathf.Min(body.Length, 160);
                string preview = body.Substring(0, n).Replace('\r', ' ').Replace('\n', ' ');
                if (body.Length > n) preview += "…";
                onError?.Invoke("JSON解析失败: " + e.Message + " | 响应片段: " + preview);
            }
        }

        public void GetActivitiesV21(Action<ActivitiesResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/activities", onSuccess, onError));

        public void ExecuteActivityV21(string activityId, string courseId,
            Action<ActivityResultV21> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(activityId))
            {
                onError?.Invoke("activityId 为空");
                return;
            }

            string body = string.IsNullOrEmpty(courseId)
                ? $"{{\"activity_id\":\"{activityId}\"}}"
                : $"{{\"activity_id\":\"{activityId}\",\"course_id\":\"{courseId}\"}}";

            StartCoroutine(PostRequestRawJson("/activities/execute", body, onSuccess, onError));
        }

        /// <summary>POST /activities/execute，带 flags（v2.2，如 sleep + curfew_penalty）。</summary>
        public void ExecuteActivityV21WithFlags(string activityId, string[] flags,
            Action<ActivityResultV21> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(activityId))
            {
                onError?.Invoke("activityId 为空");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"activity_id\":\"").Append(EscapeJson(activityId)).Append("\"");
            if (flags != null && flags.Length > 0)
            {
                sb.Append(",\"flags\":[");
                for (int i = 0; i < flags.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append('"').Append(EscapeJson(flags[i])).Append('"');
                }
                sb.Append(']');
            }
            sb.Append('}');
            StartCoroutine(PostRequestRawJson("/activities/execute", sb.ToString(), onSuccess, onError));
        }

        public void GetNPCsV21(Action<NPCListResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/npcs", onSuccess, onError));

        public void ChatV21(string npcId, string message, Action<ChatResponseV21> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                onError?.Invoke("npcId 为空");
                return;
            }

            string safe = EscapeJson(message ?? "");
            string body = $"{{\"npc_id\":\"{npcId}\",\"message\":\"{safe}\"}}";
            StartCoroutine(PostRequestRawJson("/chat", body, onSuccess, onError));
        }

        public void GetAvailableCoursesV21(Action<CoursesAvailableResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/courses/available", onSuccess, onError));

        public void GetScheduleV21(Action<ScheduleResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/courses/schedule", onSuccess, onError));

        public void GetMyCoursesV21(Action<MyCoursesResponseV21> onSuccess, Action<string> onError = null)
        {
            int semWhenRequested = PlayerManager.Instance != null ? PlayerManager.Instance.stats.semester_index : -1;
            StartCoroutine(GetRequest<MyCoursesResponseV21>("/courses/mine", data =>
            {
                MyCoursesSnapshotCache.Record(semWhenRequested, data);
                onSuccess?.Invoke(data);
            }, onError));
        }

        /// <summary>仅传 course_id（兼容旧调用）；服务端若需出勤与时间判定，请用带 attendance_status 的重载。</summary>
        public void AttendClassV21(string courseId, Action<AttendClassResponseV21> onSuccess, Action<string> onError = null) =>
            AttendClassV21(courseId, null, 0, 0, false, onSuccess, onError);

        /// <summary>
        /// POST /class/attend：附带 attendance_status（on_time / late / absent）、day_of_week、period，
        /// 便于服务端按培养方案计出勤、掌握度并推进时间。
        /// </summary>
        public void AttendClassV21(string courseId, string attendanceStatus, int dayOfWeek, int period,
            Action<AttendClassResponseV21> onSuccess, Action<string> onError = null) =>
            AttendClassV21(courseId, attendanceStatus, dayOfWeek, period, true, onSuccess, onError);

        void AttendClassV21(string courseId, string attendanceStatus, int dayOfWeek, int period, bool sendAttendanceMeta,
            Action<AttendClassResponseV21> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(courseId))
            {
                onError?.Invoke("courseId 为空");
                return;
            }

            string body;
            if (sendAttendanceMeta && !string.IsNullOrEmpty(attendanceStatus))
            {
                body = "{\"course_id\":\"" + EscapeJson(courseId) +
                       "\",\"attendance_status\":\"" + EscapeJson(attendanceStatus) +
                       "\",\"day_of_week\":" + dayOfWeek +
                       ",\"period\":" + period + "}";
            }
            else
                body = "{\"course_id\":\"" + EscapeJson(courseId) + "\"}";

            StartCoroutine(PostRequestRawJson("/class/attend", body, onSuccess, onError));
        }

        /// <summary>POST /time/advance?minutes=N（文档 Q 调试接口），用于将时钟推进到本节下课等。</summary>
        public Coroutine AdvanceTimeMinutesV21(int minutes, Action onSuccess, Action<string> onError = null) =>
            StartCoroutine(AdvanceTimeMinutesRoutine(minutes, onSuccess, onError));

        IEnumerator AdvanceTimeMinutesRoutine(int minutes, Action onSuccess, Action<string> onError)
        {
            string path = minutes > 0 ? "/time/advance?minutes=" + minutes : "/time/advance";
            string url = ApiBase + path;
            string terr = null;
            yield return ApiTransport.SendPostEmpty(url, ApiToken, (_, e) => { terr = e; });
            if (!string.IsNullOrEmpty(terr))
            {
                onError?.Invoke(terr);
                yield break;
            }

            onSuccess?.Invoke();
            ClassPeriodAutoAbsenceMonitor.RequestScanAfterTimeChange();
        }

        public void SelectCourseV21(string courseId, ScheduleSlotV21[] schedule, Action<SelectCourseResponseV21> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(courseId))
            {
                onError?.Invoke("courseId 为空");
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"{{\"course_id\":\"{courseId}\",\"schedule\":[");
            if (schedule != null)
            {
                for (int i = 0; i < schedule.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append($"{{\"day_of_week\":{schedule[i].day_of_week},\"period\":{schedule[i].period}}}");
                }
            }
            sb.Append("]}");

            StartCoroutine(PostRequestRawJson("/courses/select", sb.ToString(), onSuccess, onError));
        }

        public void GetSocialOrgsV21(Action<SocialOrgsResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetSocialOrgsV21Routine(onSuccess, onError));

        private IEnumerator GetSocialOrgsV21Routine(Action<SocialOrgsResponseV21> onSuccess, Action<string> onError)
        {
            string url = ApiBase + "/social/orgs";
            string body = null;
            string terr = null;
            yield return ApiTransport.SendGet(url, ApiToken, (b, e) => { body = b; terr = e; });
            if (!string.IsNullOrEmpty(terr))
            {
                onError?.Invoke(terr);
                yield break;
            }
            string json = WrapRootJsonArray(body, "orgs");
            if (string.IsNullOrEmpty(json))
            {
                onError?.Invoke("/social/orgs 返回为空");
                yield break;
            }

            SocialOrgsResponseV21 r = null;
            try
            {
                r = JsonUtility.FromJson<SocialOrgsResponseV21>(json);
            }
            catch (Exception e)
            {
                r = null;
                Debug.LogWarning("[APIManager] /social/orgs JsonUtility 首轮解析: " + e.Message);
            }

            if (r != null && r.orgs != null && r.orgs.Length > 0)
            {
                onSuccess?.Invoke(r);
                yield break;
            }

            var manual = SocialOrgsJsonUtil.ExtractOrgs(json);
            if (manual.Length > 0)
            {
                onSuccess?.Invoke(new SocialOrgsResponseV21 { orgs = manual });
                yield break;
            }

            int n = Mathf.Min(json.Length, 160);
            string preview = json.Substring(0, n).Replace('\r', ' ').Replace('\n', ' ');
            if (json.Length > n) preview += "…";
            onError?.Invoke("未能解析社工组织列表（orgs 为空）。片段: " + preview);
        }

        public void JoinSocialOrgV21(string orgType, Action<JoinOrgResponseV21> onSuccess, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(orgType))
            {
                onError?.Invoke("orgType 为空");
                return;
            }

            string body = $"{{\"org_type\":\"{orgType}\"}}";
            StartCoroutine(PostRequestRawJson("/social/join", body, onSuccess, onError));
        }

        public void TryPromoteV21(Action<PromoteResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PostRequestNoBody("/social/promote", onSuccess, onError));

        public void GetSocialStatusV21(Action<SocialStatusResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequest("/social/status", onSuccess, onError));

        public void GetEndingsV21(Action<EndingsResponseV21> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetEndingsV21Routine(onSuccess, onError));

        private IEnumerator GetEndingsV21Routine(Action<EndingsResponseV21> onSuccess, Action<string> onError)
        {
            string url = ApiBase + "/endings";
            string body = null;
            string terr = null;
            yield return ApiTransport.SendGet(url, ApiToken, (b, e) => { body = b; terr = e; });
            if (!string.IsNullOrEmpty(terr))
            {
                onError?.Invoke(terr);
                yield break;
            }
            string json = WrapRootJsonArray(body, "endings");
            try
            {
                var r = JsonUtility.FromJson<EndingsResponseV21>(json);
                onSuccess?.Invoke(r);
            }
            catch (Exception e)
            {
                int n = Mathf.Min(json.Length, 160);
                string preview = json.Substring(0, n).Replace('\r', ' ').Replace('\n', ' ');
                if (json.Length > n) preview += "…";
                onError?.Invoke("JSON解析失败(/endings): " + e.Message + " | 片段: " + preview);
            }
        }

        public void ResetSaveV21(Action<ResetResponse> onSuccess, Action<string> onError = null) =>
            StartCoroutine(PostRequestNoBody("/save/reset", onSuccess, onError));

        /// <summary>GET /save/export 返回完整存档 JSON（含 friendships / schedule），勿用 JsonUtility 假定旧版结构。</summary>
        public void ExportSaveRawV21(Action<string> onSuccess, Action<string> onError = null) =>
            StartCoroutine(GetRequestRaw("/save/export", onSuccess, onError));

        public void ExportSaveV21(Action<string> onSuccess, Action<string> onError = null) =>
            ExportSaveRawV21(onSuccess, onError);

        private IEnumerator GetRequestRaw(string endpoint, Action<string> onSuccess, Action<string> onError)
        {
            string url = ApiBase + endpoint;
            string body = null;
            string terr = null;
            yield return ApiTransport.SendGet(url, ApiToken, (b, e) => { body = b; terr = e; });
            if (!string.IsNullOrEmpty(terr))
            {
                onError?.Invoke(terr);
                yield break;
            }
            if (string.IsNullOrEmpty(body))
            {
                onError?.Invoke("空响应");
                yield break;
            }
            onSuccess?.Invoke(body);
        }

        private IEnumerator PostRequestRawJson<T>(string endpoint, string jsonBody, Action<T> onSuccess, Action<string> onError)
        {
            string url = ApiBase + endpoint;
            string body = null;
            string terr = null;
            yield return ApiTransport.SendPostJson(url, ApiToken, jsonBody ?? "{}", (b, e) => { body = b; terr = e; });
            HandleTextResponse(body, terr, onSuccess, onError);
        }

        private static string EscapeJson(string s)
        {
            return (s ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }

        public void ResetGame(Action<ResetResponse> onSuccess, Action<string> onError) =>
            StartCoroutine(PostRequestNoBody("/save/reset", onSuccess, onError));

        public void ExportSave(Action<string> onSuccess, Action<string> onError) =>
            StartCoroutine(GetRequestRaw("/save/export", onSuccess, onError));
    }
}
