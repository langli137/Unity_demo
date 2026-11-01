#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;

namespace ParkInspect.EditorTools
{
    /// <summary>
    /// 一键在 XR Plug-in Management 的 Android 页签启用 ARCore Loader。
    /// 菜单：Tools → 巡检 Demo → 启用 Android ARCore
    ///
    /// 等价于手点 Project Settings → XR Plug-in Management → Android → ☑ ARCore，
    /// 走的是官方编辑器 API（XRPackageMetadataStore.AssignLoader），不会写坏配置文件。
    /// </summary>
    public static class AndroidXRSetup
    {
        const string ARCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";

        [MenuItem("Tools/巡检 Demo/启用 Android ARCore", false, 2)]
        public static void EnableARCore()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("请先退出 Play 模式", "XR 设置不能在运行状态下修改。", "好");
                return;
            }

            var group = BuildTargetGroup.Android;
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);

            if (general == null || general.Manager == null)
            {
                EditorUtility.DisplayDialog(
                    "还没有 Android 的 XR 设置",
                    "请先打开 Edit → Project Settings → XR Plug-in Management，切到 Android（小机器人）页签停留一下，" +
                    "让 Unity 生成 Android 的 XR 设置，然后再点这个菜单。",
                    "好");
                return;
            }

            bool ok = XRPackageMetadataStore.AssignLoader(general.Manager, ARCoreLoaderType, group);

            general.Manager.automaticLoading = true;
            general.Manager.automaticRunning = true;
            general.InitManagerOnStart = true;

            EditorUtility.SetDirty(general.Manager);
            EditorUtility.SetDirty(general);
            AssetDatabase.SaveAssets();

            if (ok)
                Debug.Log("[XRSetup] Android 平台已启用 ARCore Loader（Automatic Loading / Running 已打开）。");
            else
                Debug.LogWarning("[XRSetup] AssignLoader 返回 false：可能已启用过，或 com.unity.xr.arcore 未安装。请检查 XR Plug-in Management 的 Android 页签。");
        }

        /// <summary>
        /// 把 Android 的 Graphics API 固定为 OpenGLES3（移除 Vulkan）。
        /// ARCore 在部分机型上用 Vulkan 会黑屏/相机不出图，官方建议 OpenGLES3。
        /// </summary>
        [MenuItem("Tools/巡检 Demo/固定 Android 图形 API 为 OpenGLES3", false, 3)]
        public static void ForceOpenGLES3()
        {
            if (!EditorUtility.DisplayDialog("修改 Android Graphics API",
                    "将关闭 Auto Graphics API，并只保留 OpenGLES3（移除 Vulkan）。继续？", "继续", "取消"))
                return;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            AssetDatabase.SaveAssets();
            Debug.Log("[XRSetup] Android Graphics API 已固定为 OpenGLES3（已移除 Vulkan）。");
        }
    }
}
#endif
