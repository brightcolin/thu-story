using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace QinghuaStory
{
    /// <summary>
    /// 明文 HTTP：部分团结/Unity 版本在编辑器或播放器里仍会拦截 UnityWebRequest。
    /// 在非 WebGL 真机构建下对 http:// 改用 HttpClient，便于与仅提供 HTTP 的后端联调。
    /// </summary>
    internal static class ApiTransport
    {
        public static bool PreferDotNetHttp(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return !string.IsNullOrEmpty(url) &&
                   url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
#endif
        }

        public static IEnumerator SendGet(string url, string token, Action<string, string> finish)
        {
            if (PreferDotNetHttp(url))
            {
                var task = Task.Run(() => DotNetRequest(HttpMethod.Get, url, token, null));
                while (!task.IsCompleted) yield return null;
                var (body, err) = task.Result;
                finish(body, err);
                yield break;
            }

            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("X-Token", token);
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    finish(null, string.IsNullOrEmpty(req.error) ? req.downloadHandler?.text : req.error);
                else
                    finish(req.downloadHandler?.text, null);
            }
        }

        public static IEnumerator SendPostJson(string url, string token, string jsonBody, Action<string, string> finish)
        {
            if (PreferDotNetHttp(url))
            {
                var task = Task.Run(() => DotNetRequest(HttpMethod.Post, url, token, jsonBody ?? "{}"));
                while (!task.IsCompleted) yield return null;
                var (body, err) = task.Result;
                finish(body, err);
                yield break;
            }

            byte[] raw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("X-Token", token);
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    finish(null, string.IsNullOrEmpty(req.error) ? req.downloadHandler?.text : req.error);
                else
                    finish(req.downloadHandler?.text, null);
            }
        }

        public static IEnumerator SendPostEmpty(string url, string token, Action<string, string> finish)
        {
            if (PreferDotNetHttp(url))
            {
                var task = Task.Run(() => DotNetRequest(HttpMethod.Post, url, token, "{}"));
                while (!task.IsCompleted) yield return null;
                var (body, err) = task.Result;
                finish(body, err);
                yield break;
            }

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("X-Token", token);
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    finish(null, string.IsNullOrEmpty(req.error) ? req.downloadHandler?.text : req.error);
                else
                    finish(req.downloadHandler?.text, null);
            }
        }

        public static IEnumerator SendPatchJson(string url, string token, string jsonBody, Action<string, string> finish)
        {
            if (PreferDotNetHttp(url))
            {
                var method = new HttpMethod("PATCH");
                var task = Task.Run(() => DotNetRequest(method, url, token, jsonBody ?? "{}"));
                while (!task.IsCompleted) yield return null;
                var (body, err) = task.Result;
                finish(body, err);
                yield break;
            }

            byte[] raw = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            using (var req = new UnityWebRequest(url, "PATCH"))
            {
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("X-Token", token);
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    finish(null, string.IsNullOrEmpty(req.error) ? req.downloadHandler?.text : req.error);
                else
                    finish(req.downloadHandler?.text, null);
            }
        }

        static (string body, string err) DotNetRequest(HttpMethod method, string url, string token, string jsonOrEmpty)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
                using var req = new HttpRequestMessage(method, url);
                req.Headers.TryAddWithoutValidation("X-Token", token);
                if (method == HttpMethod.Post || method.Method == "PATCH")
                {
                    req.Content = new StringContent(jsonOrEmpty ?? "", Encoding.UTF8, "application/json");
                }

                var resp = client.SendAsync(req).ConfigureAwait(false).GetAwaiter().GetResult();
                var text = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    try
                    {
                        var er = JsonUtility.FromJson<ErrorResponse>(text);
                        if (er != null && !string.IsNullOrEmpty(er.detail))
                            return (null, er.detail);
                    }
                    catch { /* ignore */ }
                    return (null, string.IsNullOrEmpty(text) ? resp.StatusCode.ToString() : text);
                }

                return (text, null);
            }
            catch (Exception e)
            {
                return (null, e.Message);
            }
        }
    }
}
