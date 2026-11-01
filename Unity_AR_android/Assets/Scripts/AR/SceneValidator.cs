using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using ParkInspect.UI;

namespace ParkInspect.AR
{
    /// <summary>
    /// 场景自检 + AR 可用性检查（FR-U-01/FR-U-07 的失败可见性）：
    /// 1. 启动时校验场景必需组件是否接线（缺失则 OnGUI 红字 + Debug.LogError，不依赖 Canvas，绝不上白屏）；
    /// 2. 驱动 ARSession 可用性检查（CheckAvailability / Install），跟踪 ARSession.state 给出进度与失败提示；
    /// 3. 设备不支持 AR 时明确提示"无法使用巡检上报"。
    /// 本组件只做检查与提示，不替代 ARSession/PlaneAnchorPlacer 的职责。
    /// </summary>
    public class SceneValidator : MonoBehaviour
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> infos = new List<string>();

        void OnEnable()
        {
            ARSession.stateChanged += OnSessionStateChanged;
        }

        void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;
        }

        void Start()
        {
            ValidateScene();
            StartCoroutine(CheckArAvailability());
        }

        /// <summary>组件接线自检：缺什么直接列出来，便于首次搭建排障。</summary>
        void ValidateScene()
        {
            errors.Clear();
            if (Object.FindObjectOfType<ARSession>() == null)
                AddError("缺少 ARSession 组件");
            if (Object.FindObjectOfType<ARRaycastManager>() == null)
                AddError("缺少 ARRaycastManager（需挂在 XROrigin 下）");
            if (Object.FindObjectOfType<ARPlaneManager>() == null)
                AddError("缺少 ARPlaneManager（需配置为水平面检测）");
            if (Object.FindObjectOfType<EventSystem>() == null)
                AddError("缺少 EventSystem（按钮/InputField 及 UI 点击防护依赖它）");
            if (Object.FindObjectOfType<ReportPanel>() == null)
                AddError("场景中没有 ReportPanel");
            if (Object.FindObjectOfType<PlaneAnchorPlacer>() == null)
                AddError("场景中没有 PlaneAnchorPlacer");
        }

        /// <summary>驱动 AR 环境可用性检查（标准 AR Foundation 流程），进度与结果经 stateChanged 汇报。</summary>
        IEnumerator CheckArAvailability()
        {
            if (Object.FindObjectOfType<ARSession>() == null) yield break;

            SetInfo("正在检查 AR 支持情况…");
            yield return ARSession.CheckAvailability();
            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                SetInfo("正在安装 AR 环境…");
                yield return ARSession.Install();
            }
            // 后续状态由 OnSessionStateChanged 处理（Unsupported 会显示错误）。
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            switch (args.state)
            {
                case ARSessionState.Unsupported:
                    AddError("当前设备不支持 AR（ARCore 不可用），无法使用巡检上报");
                    break;
                case ARSessionState.NeedsInstall:
                    SetInfo("需要安装 AR 环境…");
                    break;
                case ARSessionState.Installing:
                    SetInfo("正在安装 AR 环境…");
                    break;
                case ARSessionState.SessionInitializing:
                    SetInfo("正在初始化 AR 会话…");
                    break;
                case ARSessionState.SessionTracking:
                    SetInfo(""); // 进入追踪：清除进度提示
                    break;
            }
        }

        void AddError(string msg)
        {
            if (errors.Contains(msg)) return;
            errors.Add(msg);
            Debug.LogError("[SceneValidator] " + msg);
        }

        void SetInfo(string msg)
        {
            infos.Clear();
            if (!string.IsNullOrEmpty(msg)) infos.Add(msg);
        }

        /// <summary>OnGUI 兜底渲染：即使 Canvas/ReportPanel 引用断裂也能看到问题（无白屏、无静默失败）。</summary>
        void OnGUI()
        {
            if (errors.Count == 0 && infos.Count == 0) return;

            const float pad = 8f;
            float y = pad;
            foreach (var e in errors)
            {
                GUI.contentColor = Color.red;
                GUI.Label(new Rect(pad, y, Screen.width - pad * 2, 64), "⚠ " + e);
                y += 40;
            }
            foreach (var i in infos)
            {
                GUI.contentColor = Color.yellow;
                GUI.Label(new Rect(pad, y, Screen.width - pad * 2, 40), i);
                y += 28;
            }
            GUI.contentColor = Color.white;
        }
    }
}
