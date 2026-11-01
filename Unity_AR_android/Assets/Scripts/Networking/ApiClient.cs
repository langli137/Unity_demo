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
    /// 协议错误（4xx/5xx）不重试——后端明确拒绝时重试无意义，直接把 message 抛给 UI。
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
        /// 带重试的请求协程。重试仅针对网络层失败（连接超时/中断）；
        /// 服务器已应答（无论成功还是 4xx/5xx）都直接返回。
        /// </summary>
        public System.Collections.IEnumerator Send(
            string method, string path, string json, Action<int, string, IssueResult> onDone)
        {
            string url = BaseUrl + path;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                using (var req = new UnityWebRequest(url, method))
                {
                    if (json != null)
                        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                    req.timeout = TimeoutSeconds;

                    yield return req.SendWebRequest();

                    // 服务器已应答：解析响应壳后直接结束（不重试）。
                    if (req.result == UnityWebRequest.Result.Success ||
                        req.result == UnityWebRequest.Result.ProtocolError)
                    {
                        Parse(req.downloadHandler.text, req.responseCode, onDone);
                        yield break;
                    }

                    // 网络层失败：还有机会则重试一次。
                    if (attempt < MaxAttempts)
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                        continue;
                    }
                    onDone?.Invoke(-1, "网络请求失败（超时或无法连接服务器），请检查后端地址与网络", null);
                }
            }
        }

        /// <summary>解包统一响应壳。响应非法时给出可读 message，绝不静默失败。</summary>
        static void Parse(string body, long httpStatus, Action<int, string, IssueResult> onDone)
        {
            ApiResponse shell = null;
            if (!string.IsNullOrEmpty(body))
            {
                try { shell = JsonUtility.FromJson<ApiResponse>(body); }
                catch (Exception) { shell = null; }
            }

            if (shell == null)
            {
                onDone?.Invoke((int)(httpStatus != 0 ? httpStatus : -1), "后端响应无法解析", null);
                return;
            }
            onDone?.Invoke(shell.code, shell.message, shell.data);
        }
    }
}
