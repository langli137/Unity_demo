using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ParkInspect.Data;

namespace ParkInspect.Networking
{
    /// <summary>
    /// 统一 HTTP 客户端（FR-U-09）：所有请求仅经由此处发出，不散拼 HTTP。
    /// - 超时 10 秒；网络失败自动重试 1 次。
    /// - 请求/响应走统一响应壳 {code,message,data}，code==0 成功。
    /// - 协议错误（4xx/5xx）不重试——后端明确拒绝时重试无意义，直接把 message 抛给 UI。
    /// - A1 登录：Login 取 token 后存入 Token，后续请求自动带 Authorization: Bearer。
    /// 注意：本类仅组装协程，实际 yield 由 MonoBehaviour（ReportPanel）启动。
    /// </summary>
    public class ApiClient
    {
        /// <summary>
        /// 后端基地址。真机须改为运行后端的局域网 IP（README 第 7 节），例如 http://192.168.1.50:8083。
        /// </summary>
        public string BaseUrl = "http://localhost:8083";

        public const int TimeoutSeconds = 10;
        public const int MaxAttempts = 2; // 1 次原始请求 + 1 次重试

        /// <summary>
        /// 登录令牌（A1）：Login 成功后写入；非空时所有请求携带 Authorization: Bearer &lt;token&gt;。
        /// 进程内保存（App 重开需重新登录，后端 token TTL 7 天）。
        /// </summary>
        public string Token { get; set; }

        public ApiClient(string baseUrl)
        {
            if (!string.IsNullOrEmpty(baseUrl)) BaseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>POST 创建问题。onDone 携带解包后的 code/message/data。</summary>
        public System.Collections.IEnumerator PostIssue(
            IssueDraft draft, Action<int, string, IssueResult> onDone)
        {
            string body = JsonUtility.ToJson(draft);
            yield return Send("POST", "/api/v1/issues", body, onDone);
        }

        /// <summary>
        /// 登录（A1）：POST /api/v1/auth/login。成功时 data={token,user}，
        /// 成功后自动写入 Token 供后续请求携带。失败（401）data 为 null，message 可直接展示。
        /// </summary>
        public System.Collections.IEnumerator Login(
            string username, string password, Action<int, string, LoginData> onDone)
        {
            string json = JsonUtility.ToJson(new LoginRequest(username, password));
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            long status = 0;
            string text = null;
            bool networkFail = false;
            yield return Request("POST", "/api/v1/auth/login", bytes,
                "application/json; charset=utf-8",
                (s, b, n) => { status = s; text = b; networkFail = n; });

            if (networkFail)
            {
                onDone?.Invoke(-1, "网络请求失败，无法登录（请检查后端地址与网络）", null);
                yield break;
            }

            LoginResponse shell = TryParse<LoginResponse>(text);
            if (shell == null)
            {
                onDone?.Invoke(status != 0 ? (int)status : -1, "后端响应无法解析", null);
                yield break;
            }
            if (shell.code == 0 && shell.data != null && !string.IsNullOrEmpty(shell.data.token))
                Token = shell.data.token;
            onDone?.Invoke(shell.code, shell.message, shell.data);
        }

        /// <summary>
        /// 带重试的请求协程。重试仅针对网络层失败（连接超时/中断）；
        /// 服务器已应答（无论成功还是 4xx/5xx）都直接返回。
        /// </summary>
        public System.Collections.IEnumerator Send(
            string method, string path, string json, Action<int, string, IssueResult> onDone)
        {
            byte[] bytes = json == null ? null : Encoding.UTF8.GetBytes(json);
            long status = 0;
            string text = null;
            bool networkFail = false;
            yield return Request(method, path, bytes, "application/json; charset=utf-8",
                (s, b, n) => { status = s; text = b; networkFail = n; });

            if (networkFail)
            {
                onDone?.Invoke(-1, "网络请求失败（超时或无法连接服务器），请检查后端地址与网络", null);
                yield break;
            }
            Parse(text, status, onDone);
        }

        /// <summary>
        /// 底层请求（带重试）。回调 (httpStatus, body, networkFail)：
        /// networkFail=true 表示网络层失败（status/body 无意义）；
        /// 否则服务器已应答，status 为 HTTP 状态码、text 为响应体。
        /// </summary>
        System.Collections.IEnumerator Request(
            string method, string path, byte[] body, string contentType,
            Action<long, string, bool> onDone)
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                string url = BaseUrl + path;
                using (var req = new UnityWebRequest(url, method))
                {
                    if (body != null)
                        req.uploadHandler = new UploadHandlerRaw(body);
                    req.downloadHandler = new DownloadHandlerBuffer();
                    if (!string.IsNullOrEmpty(contentType))
                        req.SetRequestHeader("Content-Type", contentType);
                    // A1：登录后的请求一律携带令牌（后端 RequireAuth 解析 Bearer）。
                    if (!string.IsNullOrEmpty(Token))
                        req.SetRequestHeader("Authorization", "Bearer " + Token);
                    req.timeout = TimeoutSeconds;

                    yield return req.SendWebRequest();

                    // 服务器已应答：直接结束（不重试）。
                    if (req.result == UnityWebRequest.Result.Success ||
                        req.result == UnityWebRequest.Result.ProtocolError)
                    {
                        onDone?.Invoke(req.responseCode, req.downloadHandler.text, false);
                        yield break;
                    }

                    // 网络层失败：还有机会则重试一次。
                    if (attempt < MaxAttempts)
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                        continue;
                    }
                    onDone?.Invoke(0, null, true);
                }
            }
        }

        /// <summary>解包统一响应壳。响应非法时给出可读 message，绝不静默失败。</summary>
        static void Parse(string body, long httpStatus, Action<int, string, IssueResult> onDone)
        {
            ApiResponse shell = TryParse<ApiResponse>(body);
            if (shell == null)
            {
                onDone?.Invoke((int)(httpStatus != 0 ? httpStatus : -1), "后端响应无法解析", null);
                return;
            }
            onDone?.Invoke(shell.code, shell.message, shell.data);
        }

        /// <summary>JsonUtility 解析兜底：空串/非法 JSON 返回 null（各调用方自行报错）。</summary>
        static T TryParse<T>(string body) where T : class
        {
            if (string.IsNullOrEmpty(body)) return null;
            try { return JsonUtility.FromJson<T>(body); }
            catch (Exception) { return null; }
        }
    }
}
