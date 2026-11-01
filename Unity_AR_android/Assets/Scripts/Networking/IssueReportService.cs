using System;
using System.Collections;
using ParkInspect.Data;

namespace ParkInspect.Networking
{
    /// <summary>
    /// 上报用例编排（纯逻辑层，不继承 MonoBehaviour）：
    /// 校验表单 → 经 ApiClient 提交 → 产出给 UI 的结果状态。
    /// 视图层（ReportPanel）只负责 StartCoroutine 驱动本类，不直接拼请求。
    /// </summary>
    public class IssueReportService
    {
        /// <summary>一次提交的完整结果：任何路径都必须有可见提示（FR-U-07）。</summary>
        public struct SubmitOutcome
        {
            public bool Success;
            /// <summary>成功时服务端生成的 id。</summary>
            public string IssueId;
            /// <summary>失败时的展示文案（后端 message 或网络错误说明）。</summary>
            public string Message;
            /// <summary>true 表示网络层失败（可引导重试）；false 表示后端明确拒绝。</summary>
            public bool NetworkFailure;
        }

        readonly ApiClient api;

        /// <summary>提交中标志：防止重复点击（FR-U-07 静默失败防线之一）。</summary>
        public bool Submitting { get; private set; }

        public IssueReportService(ApiClient api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        /// <summary>
        /// 提交协程：由 ReportPanel 以 StartCoroutine 驱动。
        /// 所有路径都会回调一次 onCompleted（成功/后端报错/网络失败/本地校验失败），
        /// UI 必须给出可见提示，不得静默吞掉。
        /// </summary>
        public IEnumerator SubmitRoutine(IssueDraft draft, Action<SubmitOutcome> onCompleted)
        {
            if (Submitting)
            {
                onCompleted?.Invoke(new SubmitOutcome { Success = false, Message = "正在提交中，请稍候" });
                yield break;
            }
            if (draft == null)
            {
                onCompleted?.Invoke(new SubmitOutcome { Success = false, Message = "上报内容不完整" });
                yield break;
            }
            if (!draft.IsValid(out string error))
            {
                onCompleted?.Invoke(new SubmitOutcome { Success = false, Message = error ?? "上报内容不完整" });
                yield break;
            }

            Submitting = true;
            bool finished = false;
            SubmitOutcome outcome = default;

            yield return api.PostIssue(draft, (code, message, data) =>
            {
                if (code == 0 && data != null && !string.IsNullOrEmpty(data.id))
                {
                    outcome = new SubmitOutcome
                    {
                        Success = true,
                        IssueId = data.id,
                        Message = "提交成功",
                    };
                }
                else if (code == -1)
                {
                    // ApiClient 的网络失败分支（超时/无法连接，已含 1 次重试）。
                    outcome = new SubmitOutcome
                    {
                        Success = false,
                        NetworkFailure = true,
                        Message = string.IsNullOrEmpty(message) ? "网络请求失败，请稍后重试" : message,
                    };
                }
                else
                {
                    // 后端明确报错（400/409/500 等）：直接展示其 message。
                    outcome = new SubmitOutcome
                    {
                        Success = false,
                        Message = string.IsNullOrEmpty(message)
                            ? "提交失败（code=" + code + "）"
                            : message,
                    };
                }
                finished = true;
            });

            Submitting = false;
            if (!finished)
            {
                // 保险：正常协程路径 finished 必为 true，防止请求被外部中断后无提示。
                outcome = new SubmitOutcome { Success = false, NetworkFailure = true, Message = "请求被中断，请重试" };
            }
            onCompleted?.Invoke(outcome);
        }
    }
}
