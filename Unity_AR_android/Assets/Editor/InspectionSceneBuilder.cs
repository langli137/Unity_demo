#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ParkInspect.AR;
using ParkInspect.UI;

namespace ParkInspect.EditorTools
{
    /// <summary>
    /// 一键搭建巡检 AR 场景（对应 SETUP.md 第 3 章的全部接线步骤）。
    /// 菜单：Tools → 巡检 Demo → 一键生成 AR 场景
    ///
    /// 生成内容：
    ///   AR Session / XR Origin (Mobile AR) / ARPlaneManager(水平) / ARRaycastManager
    ///   AR Default Plane 预制件（平面可视化）/ Placer / Validator
    ///   Canvas + EventSystem + 标题/描述/上报人/优先级/提交按钮/状态提示/引导文案
    ///   并把 ReportPanel 的 7 个引用、PlaneAnchorPlacer 的 2 个引用自动连好。
    /// 产物：Assets/Scenes/ARScene.unity（自动加入 Build Settings）
    /// </summary>
    public static class InspectionSceneBuilder
    {
        const string SceneFolder = "Assets/Scenes";
        const string ScenePath = "Assets/Scenes/ARScene.unity";
        const string PrefabFolder = "Assets/Prefabs";
        const string PlanePrefabPath = "Assets/Prefabs/ARPlane.prefab";

        // 参考分辨率下的布局尺寸（Canvas 按 1080x1920 缩放）
        // v1.3 移动端改版：行高 92→116、按钮并排双列（拇指操作区 ≥110px 高），
        // 解决"按钮太小不方便操作"的反馈。
        const float FormHeight = 1020f;
        const float RowHeight = 116f;
        const float SideMargin = 32f;

        [MenuItem("Tools/巡检 Demo/一键生成 AR 场景", false, 1)]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("请先退出 Play 模式", "运行状态下无法生成场景。", "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------- 1. AR 核心 ----------
            var sessionGo = CreateFromMenu("GameObject/XR/AR Session");
            if (sessionGo == null)
            {
                EditorUtility.DisplayDialog("AR Foundation 菜单不可用",
                    "找不到「GameObject/XR/AR Session」菜单。请确认已安装 com.unity.xr.arfoundation。", "好");
                return;
            }

            var originGo = CreateFromMenu("GameObject/XR/XR Origin (Mobile AR)");
            if (originGo == null)
            {
                EditorUtility.DisplayDialog("AR Foundation 菜单不可用",
                    "找不到「GameObject/XR/XR Origin (Mobile AR)」菜单。请确认已安装 com.unity.xr.arfoundation。", "好");
                return;
            }

            // 平面检测 + 射线检测（SETUP.md 3.1-3 / 3.1-4）
            // 水平 + 垂直：墙面裂缝需要在垂直面上放置标识。
            var planeManager = originGo.AddComponent<ARPlaneManager>();
            planeManager.detectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            planeManager.planePrefab = CreatePlanePrefab();

            var raycastManager = originGo.AddComponent<ARRaycastManager>();

            // ---------- 2. 放置器 ----------
            var placerGo = new GameObject("Placer");
            var placer = placerGo.AddComponent<PlaneAnchorPlacer>();
            var placerSo = new SerializedObject(placer);
            placerSo.FindProperty("raycastManager").objectReferenceValue = raycastManager;
            placerSo.FindProperty("planeManager").objectReferenceValue = planeManager;
            placerSo.FindProperty("markerPrefab").objectReferenceValue = null; // 留空 → 运行时生成 0.1m 立方体
            placerSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 3. 场景自检 ----------
            var validatorGo = new GameObject("Validator");
            validatorGo.AddComponent<SceneValidator>();

            // ---------- 4. UI ----------
            var canvasGo = BuildCanvas();
            var ui = BuildFormUI(canvasGo.transform);

            var panel = canvasGo.AddComponent<ReportPanel>();
            var panelSo = new SerializedObject(panel);
            panelSo.FindProperty("titleInput").objectReferenceValue = ui.title;
            panelSo.FindProperty("descriptionInput").objectReferenceValue = ui.description;
            panelSo.FindProperty("priorityDropdown").objectReferenceValue = ui.priority;
            panelSo.FindProperty("reporterInput").objectReferenceValue = ui.reporter;
            panelSo.FindProperty("markerSizeDropdown").objectReferenceValue = ui.markerSize;
            panelSo.FindProperty("submitButton").objectReferenceValue = ui.submit;
            panelSo.FindProperty("cancelButton").objectReferenceValue = ui.cancel;
            panelSo.FindProperty("statusText").objectReferenceValue = ui.status;
            panelSo.FindProperty("guidanceText").objectReferenceValue = ui.guidance;
            panelSo.FindProperty("placer").objectReferenceValue = placer;
            panelSo.FindProperty("baseUrl").stringValue = "http://10.216.180.140:8083"; // 换网络后同步改为电脑当前局域网 IP（手机须与电脑同一网络）
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 5. 保存 ----------
            EnsureFolder(SceneFolder);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            AddToBuildSettings(ScenePath);

            Debug.Log("[SceneBuilder] 场景已生成：" + ScenePath);
            Debug.Log("下一步（缺一不可）：\n" +
                      "1) Edit → Project Settings → XR Plug-in Management → 切到 Android 页签 → 勾选 ARCore\n" +
                      "2) File → Build Settings → Android → Switch Platform\n" +
                      "3) 选中 Canvas 上的 ReportPanel，把 Base Url 改成电脑局域网 IP（如 http://192.168.1.50:8083）\n" +
                      "4) 手机开 USB 调试并连接，点 Build And Run");

            Selection.activeGameObject = canvasGo;
        }

        // ================= UI =================

        static GameObject BuildCanvas()
        {
            var canvasGo = CreateFromMenu("GameObject/UI/Canvas");
            if (canvasGo == null)
            {
                // 兜底：手工创建 Canvas
                canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            }
            canvasGo.name = "Canvas";

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            // EventSystem（按钮点击 + UI 防穿透依赖）
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
                CreateFromMenu("GameObject/UI/Event System");

            return canvasGo;
        }

        struct FormUI
        {
            public InputField title, description, reporter;
            public Dropdown priority, markerSize;
            public Button submit, cancel;
            public Text status, guidance;
        }

        static FormUI BuildFormUI(Transform canvas)
        {
            var ui = new FormUI();

            // 顶部引导文案（FR-U-02：请缓慢移动手机 / 请点击平面放置标记）
            var guidanceGo = CreateUI("Text", "GuidanceText", canvas);
            if (guidanceGo != null)
            {
                ui.guidance = guidanceGo.GetComponent<Text>();
                ui.guidance.text = "正在检测平面，请缓慢移动手机\n（通常需要 20 秒内）";
                ui.guidance.fontSize = 52;
                ui.guidance.alignment = TextAnchor.MiddleCenter;
                ui.guidance.color = Color.white;
                ui.guidance.horizontalOverflow = HorizontalWrapMode.Wrap;
                SetRect(guidanceGo, canvas, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                    new Vector2(-SideMargin * 2, 260f), new Vector2(0, -100f));
            }

            // 底部表单面板：直接手工创建（2022.3 的 UI Legacy 菜单没有 Panel 项，
            // 走 ExecuteMenuItem 会在 Console 留一条 "no menu named" 报错，且本面板只需 RectTransform+Image）
            var panelGo = new GameObject("FormPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvas, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(1, 0);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.sizeDelta = new Vector2(0, FormHeight);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImage = panelGo.GetComponent<Image>();
            if (panelImage != null) panelImage.color = new Color(0f, 0f, 0f, 0.45f);

            float y = 16f;

            ui.title = CreateField("TitleInput", panelRect, ref y, "标题（必填）");
            ui.description = CreateField("DescriptionInput", panelRect, ref y, "描述（可选）");
            ui.reporter = CreateField("ReporterInput", panelRect, ref y, "上报人（可选）");

            // 优先级下拉
            var dropdownGo = CreateUI("Dropdown", "PriorityDropdown", panelRect);
            if (dropdownGo != null)
            {
                ui.priority = dropdownGo.GetComponent<Dropdown>();
                SetRow(dropdownGo, panelRect, ref y);
                EnlargeDropdownText(ui.priority);
            }

            // 标识框大小下拉（小 5cm / 中 10cm / 大 20cm，选项由 ReportPanel.ApplyMarkerSizeOptions 固定）
            var sizeGo = CreateUI("Dropdown", "MarkerSizeDropdown", panelRect);
            if (sizeGo != null)
            {
                ui.markerSize = sizeGo.GetComponent<Dropdown>();
                SetRow(sizeGo, panelRect, ref y);
                EnlargeDropdownText(ui.markerSize);
            }

            // 提交/取消并排双列（v1.3）：一行放两个大按钮，便于单手拇指操作。
            // 左：提交上报（主操作，宽 60%）；右：取消标识（辅助，宽 40%）。
            var buttonGo = CreateUI("Button", "SubmitButton", panelRect);
            var cancelGo = CreateUI("Button", "CancelButton", panelRect);
            if (buttonGo != null && cancelGo != null)
            {
                ui.submit = buttonGo.GetComponent<Button>();
                ui.cancel = cancelGo.GetComponent<Button>();
                SetButtonRow(buttonGo, cancelGo, panelRect, ref y);
                var submitLabel = buttonGo.GetComponentInChildren<Text>();
                if (submitLabel != null) { submitLabel.text = "提交上报"; submitLabel.fontSize = 42; }
                var cancelLabel = cancelGo.GetComponentInChildren<Text>();
                if (cancelLabel != null) { cancelLabel.text = "取消标识"; cancelLabel.fontSize = 42; }
            }
            else
            {
                // 兜底：控件创建失败时逐个处理，保持接线不缺失。
                if (buttonGo != null)
                {
                    ui.submit = buttonGo.GetComponent<Button>();
                    SetRow(buttonGo, panelRect, ref y);
                    var label = buttonGo.GetComponentInChildren<Text>();
                    if (label != null) { label.text = "提交上报"; label.fontSize = 42; }
                }
                if (cancelGo != null)
                {
                    ui.cancel = cancelGo.GetComponent<Button>();
                    SetRow(cancelGo, panelRect, ref y);
                    var label = cancelGo.GetComponentInChildren<Text>();
                    if (label != null) { label.text = "取消标识"; label.fontSize = 48; }
                }
            }

            // 状态提示（成功/失败必须可见，FR-U-07）
            var statusGo = CreateUI("Text", "StatusText", panelRect);
            if (statusGo != null)
            {
                ui.status = statusGo.GetComponent<Text>();
                ui.status.text = "";
                ui.status.fontSize = 38;
                ui.status.alignment = TextAnchor.MiddleLeft;
                ui.status.color = Color.white;
                SetRow(statusGo, panelRect, ref y, 70f);
            }

            return ui;
        }

        static InputField CreateField(string name, Transform parent, ref float y, string placeholder)
        {
            var go = CreateUI("Input Field", name, parent);
            if (go == null) return null;

            var field = go.GetComponent<InputField>();
            SetRow(go, parent, ref y);

            // 2022.3 起 Legacy InputField 的正文子物体叫 "Text (Legacy)"，
            // 旧代码 Find("Text") 返回 null → 正文保持默认 14px（真机上几乎不可读）。
            var text = go.transform.Find("Text") ?? go.transform.Find("Text (Legacy)");
            if (text != null)
            {
                var t = text.GetComponent<Text>();
                if (t != null) { t.fontSize = 48; t.alignment = TextAnchor.MiddleLeft; }
            }
            var ph = go.transform.Find("Placeholder");
            if (ph != null)
            {
                var t = ph.GetComponent<Text>();
                if (t != null) { t.text = placeholder; t.fontSize = 48; }
            }
            return field;
        }

        /// <summary>放大下拉框的当前值文字与选项文字（Unity 默认 14px 在手机上不可读，v1.3 移动端改版）。</summary>
        static void EnlargeDropdownText(Dropdown dropdown)
        {
            if (dropdown == null) return;
            // 当前值标签（"Label" 子物体）
            var label = dropdown.transform.Find("Label");
            if (label != null)
            {
                var t = label.GetComponent<Text>();
                if (t != null) { t.fontSize = 38; t.alignment = TextAnchor.MiddleLeft; }
            }
            // 选项列表：Item 行加高 + 选项字号放大（每行可点区域更大）
            var template = dropdown.template;
            if (template != null)
            {
                var itemRt = dropdown.itemText.transform.parent as RectTransform;
                if (itemRt != null)
                {
                    var sd = itemRt.sizeDelta;
                    itemRt.sizeDelta = new Vector2(sd.x, 96f);
                }
                dropdown.itemText.fontSize = 36;
                // 展开列表整体加高，一屏可见三个选项。
                var tsd = template.sizeDelta;
                template.sizeDelta = new Vector2(tsd.x, 380f);
            }
        }

        const float RowGap = 12f;
        /// <summary>并排双列按钮之间的间距。</summary>
        const float ColumnGap = 16f;

        static void SetRow(GameObject go, Transform parent, ref float y, float height = RowHeight)
        {
            SetRect(go, parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                new Vector2(-SideMargin * 2, height), new Vector2(0, -y));
            y += height + RowGap;
        }

        /// <summary>一行放两个按钮：左 primary 占 60% 宽，右 secondary 占 40%（v1.3 移动端拇指操作布局）。</summary>
        static void SetButtonRow(GameObject left, GameObject right, Transform parent, ref float y,
            float height = RowHeight + 12f)
        {
            float leftFrac = 0.6f;
            float half = SideMargin + ColumnGap / 2f;

            SetRect(left, parent, new Vector2(0, 1), new Vector2(leftFrac, 1), new Vector2(0.5f, 1f),
                new Vector2(-half, height), new Vector2(0, -y));
            SetRect(right, parent, new Vector2(leftFrac, 1), new Vector2(1, 1), new Vector2(0.5f, 1f),
                new Vector2(-half, height), new Vector2(0, -y));
            y += height + RowGap;
        }

        static void SetRect(GameObject go, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 sizeDelta, Vector2 anchoredPos)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            if (rt.parent != parent) rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;
        }

        /// <summary>用 uGUI 自带菜单创建控件，保证结构（Dropdown 的 Template / InputField 的 Text）正确。</summary>
        static GameObject CreateUI(string legacyName, string finalName, Transform parent)
        {
            var go = CreateFromMenu("GameObject/UI/Legacy/" + legacyName) ?? CreateFromMenu("GameObject/UI/" + legacyName);
            if (go == null)
            {
                Debug.LogError("[SceneBuilder] 创建 UI 控件失败：" + legacyName + "（com.unity.ugui 是否已安装？）");
                return null;
            }
            go.name = finalName;
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject CreateFromMenu(string menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath)) return null;
            return Selection.activeGameObject;
        }

        // ================= 资源 =================

        /// <summary>创建 AR Default Plane 可视化预制件并返回资源引用（FR-U-02 平面可视化）。</summary>
        static GameObject CreatePlanePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (existing != null) return existing;

            var temp = CreateFromMenu("GameObject/XR/AR Default Plane");
            if (temp == null) return null;

            EnsureFolder(PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, PlanePrefabPath, out bool success);
            Object.DestroyImmediate(temp);
            AssetDatabase.Refresh();
            return success ? prefab : null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && parent != "Assets") EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
#endif
