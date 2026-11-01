using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ParkInspect.AR
{
    /// <summary>标识状态：None 无 / Placed 待提交（黄色，锁定不可移动）/ Submitted 已提交（红色现场记录）。</summary>
    public enum MarkerState { None, Placed, Submitted }

    /// <summary>
    /// 放置失败原因：用于把"点了没反应"变成可见反馈（用户反馈的核心问题——静默失败）。
    /// </summary>
    public enum PlaceFail
    {
        None,          // 放置成功
        AlreadyPlaced, // 已有待提交标记（一次一个，不再移动）
        OverUi,        // 点在按钮/输入框上（不提示，属正常 UI 交互）
        NoPlaneHit,    // 点击处射线未落进任何平面多边形（可能超出识别区域边界）
        NoUsablePlane, // 命中的平面还没收敛/已丢失跟踪（继续扫描即可）
    }

    /// <summary>
    /// 平面命中与标记放置（FR-U-02/03/08，v1.3 交互改版）：
    /// - 屏幕点命中平面（水平+垂直）才放置标记；
    /// - 放置即锁定：不再响应"点击移动"（v1.2 前的点击移动会被手持误触触发，导致标记跟着手机动）；
    /// - 一次只能有一个待提交标记（黄色）；提交成功后转红色（Submitted）作为现场记录，可继续放置下一个；
    /// - 长按标记（ReportPanel 检测）可弹确认框取消，取消后才能重新放置；
    /// - UI 穿透防护：点击落在按钮/输入框上时不放置标记。
    /// </summary>
    public class PlaneAnchorPlacer : MonoBehaviour
    {
        [SerializeField] ARRaycastManager raycastManager;
        [SerializeField] ARPlaneManager planeManager;
        [SerializeField] MarkerVisual markerPrefab; // 为空时运行时生成默认立方体

        GameObject activeMarker;                 // 待提交标记（黄色，同一时刻至多一个）
        readonly List<GameObject> submittedMarkers = new List<GameObject>(); // 已提交标记（红色现场记录）
        string currentPlaneId = "";
        float markerEdge = 0.1f;

        static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
        /// <summary>待提交标记颜色（黄）。</summary>
        static readonly Color PlacedColor = new Color(1f, 0.82f, 0.1f);
        /// <summary>已提交标记颜色（红，用户要求：被提交的显示为红色标识）。</summary>
        static readonly Color SubmittedColor = new Color(0.86f, 0.13f, 0.13f);

        /// <summary>
        /// 判定平面"可用"的最小尺寸（米，短边）。
        /// ARCore 刚起的初步面往往很小很粗（几厘米、还在收敛），把它当作"已识别"就是之前
        /// "识别依据不正常"的根源——用户会在还没收敛的斜面上放标识，位置自然飘。
        /// 只有短边达到该阈值的平面才计入"已检测到平面"并允许放置。
        /// </summary>
        const float MinPlaneShortSide = 0.12f;

        /// <summary>当前标识状态。</summary>
        public MarkerState State =>
            activeMarker != null ? MarkerState.Placed :
            submittedMarkers.Count > 0 ? MarkerState.Submitted : MarkerState.None;
        /// <summary>是否存在待提交标记。</summary>
        public bool HasMarker => activeMarker != null;
        public float MarkerEdge => markerEdge;
        /// <summary>待提交标记的世界坐标（提交体 position 取值来源，FR-U-06）。</summary>
        public Vector3 MarkerPosition => activeMarker != null ? activeMarker.transform.position : Vector3.zero;
        /// <summary>待提交标记的旋转（提交体 anchor.rotation 取值来源）。</summary>
        public Vector3 MarkerRotation => activeMarker != null ? activeMarker.transform.rotation.eulerAngles : Vector3.zero;
        public string PlaneId => currentPlaneId;

        /// <summary>
        /// 已检测到的"可用"平面数量（供 UI 引导文案，FR-U-02）。
        /// 只统计仍在跟踪且尺寸收敛的平面，不把 ARCore 刚生成、还在漂移的初步碎面算作"已检测到平面"。
        /// </summary>
        public int PlaneCount
        {
            get
            {
                int n = 0;
                if (planeManager != null)
                {
                    foreach (var plane in planeManager.trackables)
                    {
                        if (IsUsablePlane(plane)) n++;
                    }
                }
                return n;
            }
        }

        /// <summary>
        /// 平面是否"可用"（识别依据）：平面仍在跟踪中（未丢失）且短边尺寸达到阈值。
        /// - 尺寸阈值是"已收敛"的主信号：ARCore 初步面只有几厘米，长到 MinPlaneShortSide 以上才是真实表面；
        /// - 跟踪态放宽为"未 None"（TrackingState 合法值仅 None/Limited/Tracking）：低纹理环境下 ARCore 会把平面频繁降为 Limited，
        ///   若强求 ==Tracking 会出现"平面明明显示着却点不上"（用户反馈的点击没反应），故只排除已丢失（None）的面。
        /// </summary>
        static bool IsUsablePlane(ARPlane plane)
        {
            if (plane == null) return false;
            // 已丢失跟踪（None）的平面不可放置（trackables 通常会移除，此处防御）。
            if (plane.trackingState == TrackingState.None) return false;
            // 短边尺寸达到阈值才算收敛（ARPlane.size 为平面在自身坐标系下的宽×高，米）。
            return Mathf.Min(plane.size.x, plane.size.y) >= MinPlaneShortSide;
        }

        /// <summary>
        /// 尝试在屏幕位置放置标记（v1.3：仅放置，不再移动已有标记）。
        /// 已有待提交标记时返回 false（一次只能一个），ReportPanel 会提示长按取消。
        /// pointerId：触摸传 touch.fingerId，鼠标传 -1（编辑器）。
        /// </summary>
        public bool TryPlace(Vector2 screenPos, int pointerId = -1)
        {
            return TryPlace(screenPos, pointerId, out _);
        }

        /// <summary>
        /// 尝试放置标记，并通过 fail 输出失败原因——让 ReportPanel 把"点了没反应"变成可见反馈。
        /// </summary>
        public bool TryPlace(Vector2 screenPos, int pointerId, out PlaceFail fail)
        {
            // 一次只能一个待提交标记：锁定后误触/再次点击都不会改变位置。
            if (activeMarker != null) { fail = PlaceFail.AlreadyPlaced; return false; }

            // UI 穿透防护（FR-U-08）：点在 UI 上直接忽略，避免误放标记。
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId))
            { fail = PlaceFail.OverUi; return false; }

            if (raycastManager == null) { fail = PlaceFail.NoPlaneHit; return false; }

            // PlaneWithinPolygon：射线须落进某平面的真实多边形内（不在识别区域内点击即 miss）。
            if (!raycastManager.Raycast(screenPos, Hits, TrackableType.PlaneWithinPolygon))
            { fail = PlaceFail.NoPlaneHit; return false; }

            // 接受水平面与垂直面命中（墙面裂缝标记场景），并要求该平面可用（跟踪中 + 尺寸收敛）。
            ARRaycastHit best = default;
            bool found = false;
            foreach (var hit in Hits)
            {
                if (!IsPlaceablePlane(hit.trackableId)) continue;
                if (!found || hit.distance < best.distance)
                {
                    best = hit;
                    found = true;
                }
            }
            if (!found) { fail = PlaceFail.NoUsablePlane; return false; }

            activeMarker = CreateMarker();
            activeMarker.transform.SetPositionAndRotation(best.pose.position, best.pose.rotation);
            currentPlaneId = best.trackableId.ToString();
            Tint(activeMarker, PlacedColor);
            fail = PlaceFail.None;
            return true;
        }

        /// <summary>
        /// 提交成功后调用：待提交标记转红并锁定为现场记录（Submitted），活动位清空可放置下一个。
        /// 与 v1.2"成功即清标识"的差异：用户 v1.3 明确要求已提交的显示为红色标识留在现场。
        /// </summary>
        public void MarkSubmitted()
        {
            if (activeMarker == null) return;
            Tint(activeMarker, SubmittedColor);
            submittedMarkers.Add(activeMarker);
            activeMarker = null;
            currentPlaneId = "";
        }

        /// <summary>取消当前待提交标记（长按确认后调用，FR-U-08 补充）。</summary>
        public void CancelMarker()
        {
            if (activeMarker != null) Destroy(activeMarker);
            activeMarker = null;
            currentPlaneId = "";
        }

        /// <summary>
        /// 长按命中检测：screenPos 附近（maxPx 像素）是否有标记，返回最近的一个。
        /// 优先返回待提交（黄）标记；isActive 返回命中的是否为待提交标记。
        /// </summary>
        public bool TryGetMarkerNear(Vector2 screenPos, float maxPx, out bool isActive)
        {
            isActive = false;
            Camera cam = Camera.main;
            if (cam == null) return false;

            float bestSqr = maxPx * maxPx;
            GameObject best = null;
            bool bestIsActive = false;

            if (activeMarker != null && ScreenDistanceSq(cam, activeMarker, screenPos, out float sqr) && sqr <= bestSqr)
            {
                best = activeMarker;
                bestSqr = sqr;
                bestIsActive = true;
            }
            foreach (var go in submittedMarkers)
            {
                if (go == null) continue;
                if (ScreenDistanceSq(cam, go, screenPos, out float s) && s <= bestSqr)
                {
                    best = go;
                    bestSqr = s;
                    bestIsActive = false;
                }
            }
            if (best == null) return false;
            isActive = bestIsActive;
            return true;
        }

        /// <summary>移除一个已提交（红色）标记（长按确认后调用）。</summary>
        public bool RemoveSubmitted(GameObject go)
        {
            if (go == null || !submittedMarkers.Contains(go)) return false;
            submittedMarkers.Remove(go);
            Destroy(go);
            return true;
        }

        /// <summary>按屏幕位置移除最近的已提交（红色）标记；命中待提交标记时不处理（返回 false）。</summary>
        public bool RemoveNearestSubmittedAt(Vector2 screenPos, float maxPx)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            float bestSqr = maxPx * maxPx;
            GameObject best = null;
            foreach (var go in submittedMarkers)
            {
                if (go == null) continue;
                if (!ScreenDistanceSq(cam, go, screenPos, out float s) || s > bestSqr) continue;
                bestSqr = s;
                best = go;
            }
            if (best == null) return false;
            submittedMarkers.Remove(best);
            Destroy(best);
            return true;
        }

        /// <summary>设置标记边长（米），立即作用于待提交标记。</summary>
        public void SetMarkerEdge(float edge)
        {
            markerEdge = Mathf.Clamp(edge, 0.02f, 2f);
            if (activeMarker != null)
                activeMarker.transform.localScale = Vector3.one * markerEdge;
        }

        static bool ScreenDistanceSq(Camera cam, GameObject go, Vector2 screenPos, out float sqr)
        {
            sqr = float.MaxValue;
            Vector3 sp = cam.WorldToScreenPoint(go.transform.position);
            if (sp.z < 0) return false; // 在相机背后
            Vector2 d = new Vector2(sp.x, sp.y) - screenPos;
            sqr = d.sqrMagnitude;
            return true;
        }

        /// <summary>染色：Cube 等基础几何体自带默认材质，renderer.material.color 在内置/URP 管线均可映射主色。</summary>
        static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            try
            {
                r.material.color = color;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PlaneAnchorPlacer] 标记染色失败（不影响放置/上报）：" + e.Message);
            }
        }

        bool IsPlaceablePlane(TrackableId id)
        {
            if (planeManager == null) return true; // 未挂载 planeManager 时不做过滤
            foreach (var plane in planeManager.trackables)
            {
                if (plane.trackableId != id) continue;
                // 识别依据：平面必须已稳定跟踪且尺寸收敛（排除 ARCore 初步斜面）。
                if (!IsUsablePlane(plane)) return false;
                // 水平（地面/桌面）与垂直（墙面）都接受：裂缝可能出现在墙面。
                return plane.alignment == PlaneAlignment.HorizontalUp ||
                       plane.alignment == PlaneAlignment.HorizontalDown ||
                       plane.alignment == PlaneAlignment.Vertical;
            }
            return false;
        }

        /// <summary>
        /// 创建标记对象。防御：真机 IL2CPP 裁剪曾导致 CreatePrimitive 因缺 BoxCollider 失败
        /// （已由 Assets/link.xml 修复），这里兜底——即使引擎类型缺失也不能让放置功能中断（FR-U-03）。
        /// </summary>
        GameObject CreateMarker()
        {
            GameObject go;
            if (markerPrefab != null)
            {
                go = Instantiate(markerPrefab.gameObject);
            }
            else
            {
                try
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    if (go.GetComponent<Collider>() == null)
                        Debug.LogWarning("[PlaneAnchorPlacer] 标记缺少碰撞体（疑似引擎代码被裁剪），" +
                            "请确认 Assets/link.xml 存在并重新构建。");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[PlaneAnchorPlacer] CreatePrimitive 失败，回退为空标记：" + e.Message);
                    go = new GameObject("IssueMarker"); // 无渲染兜底：位置/上报链路不受影响
                }
                go.transform.localScale = Vector3.one * markerEdge;
            }
            go.name = "IssueMarker";
            return go;
        }
    }

    /// <summary>标记视觉占位组件：便于在 Inspector 中引用预制件。</summary>
    public class MarkerVisual : MonoBehaviour { }
}
