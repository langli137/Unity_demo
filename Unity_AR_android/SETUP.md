# Unity AR 端搭建与接线（Android 真机）

> 对应 `docs/需求文档.md` FR-U-01~09。本目录当前只含 `Assets/Scripts/`（纯代码）；
> Packages、ProjectSettings、场景文件需按下列步骤在 Unity 编辑器中生成。

## 0. 最快路径：一键生成场景（推荐）

场景接线已经脚本化，不必手点第 3 章的十几个步骤：

1. 用 Unity 打开本目录，等待编译完成（菜单栏出现 `Tools/巡检 Demo`）。
2. 点 **Tools → 巡检 Demo → 一键生成 AR 场景**。
3. 生成 `Assets/Scenes/ARScene.unity`（自动加入 Build Settings），包含：
   `AR Session`、`XR Origin (Mobile AR)`（含 ARCameraManager / ARCameraBackground / TrackedPoseDriver）、
   `ARPlaneManager`（Horizontal + Vertical + AR Default Plane 可视化）、`ARRaycastManager`、`Placer`、`Validator`、
   `Canvas`（标题/描述/上报人/优先级/标识大小/提交/取消标识/状态/引导）+ `EventSystem`，
   并已自动连好 `ReportPanel` 的 9 个引用与 `PlaneAnchorPlacer` 的 2 个引用。
4. 仍需**手工两步**（脚本无法代劳）：
   - `Edit → Project Settings → XR Plug-in Management` → 切 **Android 页签** → 勾 **ARCore**；
   - 后端地址（二选一，见第 4 节）：USB 联调保持默认 `http://localhost:8083` 并执行 `adb reverse tcp:8083 tcp:8083`；无线联调改成电脑局域网 IP。

> 生成脚本：`Assets/Editor/InspectionSceneBuilder.cs`。手写场景的原始步骤仍保留在第 3 章，供核对或改造。

## 1. 工程与依赖

1. Unity Hub 新建工程（推荐 **Unity 2022.3 LTS**，3D 模板），把本目录 `Assets/Scripts/` 整体拷入其 `Assets/`（或直接用本目录作为工程目录打开）。
2. Package Manager 安装：
   - `com.unity.xr.arfoundation`（AR Foundation 5.x）
   - `com.unity.xr.arcore`（ARCore XR Plugin）
   - `com.unity.ugui`（UI，默认已装）
3. **本工程 `Packages/manifest.json` 已按 Unity 2022.3.62f3c1 写好依赖**（arfoundation/arcore 5.1.5、ugui 1.0.0、xr.management 4.4.0），打开工程后等待 Package Manager 自动解析即可，无需手动安装；若解析失败，改用编辑器对应版本的 Package Manager 安装上述三包（版本以编辑器实际解析为准，**不要照抄版本号**）。

## 2. Player / Android 设置

| 项 | 值 | 原因 |
|---|---|---|
| Active Input Handling | **Input Manager (Old)** 或 Both | 脚本使用 `Input.GetTouch/GetMouseButtonDown` |
| XR Plug-in Management（Android 页签） | 勾选 **ARCore** | 不勾选则 ARSession 无法启动（`ARSessionState.Unsupported`） |
| Internet Access | Enabled（默认） | 提交到后端 |
| Camera Usage Description | 如 `AR巡检需要相机` | 相机权限说明 |
| Minimum API Level | 24+ | ARCore 要求 |
| Graphics APIs | 移除 Vulkan 保留 OpenGLES3（如遇黑屏） | ARCore 兼容性 |
| Build Target | Android，ARM64 | 真机（FR-U-01：模拟器不算通过） |

> 明文 HTTP（`http://局域网IP:8083`）：Android 9+ 默认允许 App 自行发起的明文请求受 `usesCleartextTraffic` 影响，
> 若提交被系统拦截，在 `AndroidManifest.xml` 的 `<application>` 加 `android:usesCleartextTraffic="true"`，或后端改用 HTTPS。

## 3. 场景接线（新建场景 Scene.unity，逐步执行）

### 3.1 AR 核心
| 步骤 | 操作 |
|---|---|
| 1 | Hierarchy → XR → **AR Session**（挂 `ARSession`） |
| 2 | Hierarchy → XR → **XR Origin (Mobile AR)**（自带 `XROrigin` + Camera；给 Camera 挂 `ARCameraManager`、`ARCameraBackground`，子物体加 `ARTrackedPoseDriver` 或 `TrackedPoseDriver`） |
| 3 | XR Origin 根上加 **`ARPlaneManager`**（Detection Mode = **Horizontal + Vertical**，墙面裂缝需垂直面；Plane Prefab 可用 ARF 自带 `ARPlane` 预制件实现平面可视化——FR-U-02 的"可视化提示"） |
| 4 | XR Origin 根上加 **`ARRaycastManager`**（Raycast Mask = PlaneWithinPolygon） |
| 5 | Hierarchy 空物体 `Placer` 挂 **`PlaneAnchorPlacer`**：连线 `raycastManager` = 步骤4、`planeManager` = 步骤3、`markerPrefab` 留空（运行时生成 0.1m 立方体）或指定 `MarkerVisual` 预制件 |
| 6 | Hierarchy 空物体 `Validator` 挂 **`SceneValidator`**（无需连线；启动自检 + AR 可用性提示） |

### 3.2 UI（Canvas）
1. 创建 Canvas（Screen Space - Overlay）+ **EventSystem**（缺失时 SceneValidator 会报错；按钮点击也依赖它）。
2. 按布局放置：`titleInput`(InputField)、`descriptionInput`(InputField 多行可选)、`priorityDropdown`(Dropdown)、`reporterInput`(InputField，可选)、`markerSizeDropdown`(Dropdown，标识框大小)、`submitButton`(Button)、`cancelButton`(Button，取消标识)、`statusText`(Text，结果提示)、`guidanceText`(Text，平面引导，含检测时限提示)。
3. 挂 **`ReportPanel`**，Inspector 连线：上述 9 个引用 + `placer` = 步骤 3.1-5 的 Placer；`Base Url` 见第 4 节。
4. `priorityDropdown` 选项顺序必须是 **高(high) / 中(medium) / 低(low)**——脚本按索引映射（`Priorities = {high, medium, low}`），选项不同会导致优先级错位。同理 `markerSizeDropdown` 对应 `MarkerEdges = {0.05, 0.1, 0.2}`（小/中/大），不过 Start 时会自动重建选项，无需手配。

> 接线遗漏不会静默失败：`ReportPanel.ValidateReferences` 会在 Console 报缺失清单。

### 3.3 表单交互说明（v1.3）
- 点击 Canvas 内按钮/输入框不会放置标记（`PlaneAnchorPlacer` 的 EventSystem 防护 + ReportPanel 转发触摸时传 fingerId，FR-U-08）。
- **放置即锁定**：点击平面放置标识（黄色）后位置不再变化——手持误触/再次点击都不会移动它；
  同一时刻只允许一个待提交标识。要重放请**长按标识 0.8 秒**弹确认框取消（或点 **取消标识** 按钮）。
- **提交成功后标识转红色**（Submitted）作为现场记录留在原地；待提交位清空，可继续放置下一个标识。
  长按红色标识可弹框移除。提交取标记当前位置（FR-U-06）。
- 标识框大小分 小(5cm)/中(10cm)/大(20cm) 三档，切换立即作用于已放置的标记。
- **平面检测时限**：正常 5~20 秒；超过 30 秒（`ReportPanel.DetectTimeoutSeconds`）未检测到即显示超时提示
  与环境建议（光线充足、纹理丰富；纯白/反光/昏暗表面无法识别），不再无限累计"已等待 X 秒"。
  首次检测到平面时 Console 仍记录实际耗时。

## 4. 后端地址配置（真机联调）

- 位置：`ReportPanel` 组件 Inspector → `Base Url`。两种方式二选一：
  1. **USB 联调（推荐）**：保持默认 `http://localhost:8083`，连接手机后执行 `adb reverse tcp:8083 tcp:8083`——手机上的 localhost 直通电脑后端，不依赖 Wi-Fi/防火墙。拔插 USB 后需重新执行（`adb reverse --list` 可查看）。
  2. **无线联调**：手机与电脑同一 Wi-Fi，Base Url 改为电脑局域网 IP（如 `http://192.168.153.140:8083`），电脑防火墙放行 8083 入站。

## 5. 构建与验收

0. **前置条件**：Unity Hub → Installs → 对应版本 → Add modules，安装 **Android Build Support**（含 OpenJDK 与 Android SDK & NDK Tools）。
   未安装时 `Build Settings` 中 Android 平台不可切换（本机 `PlaybackEngines` 仅有 WebGL / Windows，需先补装）。
1. `File → Build Settings → Android → Switch Platform`；USB 连真机，`Run`。
2. 按 `docs/需求文档.md` 第 7 章 3.1 清单验收：
   - [ ] 3.1.1 真机安装进入相机场景（SceneValidator 无红字、state 进入 SessionTracking）
   - [ ] 3.1.2 平面可视化/引导文案（"正在检测平面…通常需要 20 秒内"；超 30 秒显示超时提示 →"已检测到平面，请点击裂缝位置放置标识"）
   - [ ] 3.1.3 点击平面放置黄色标识后位置锁定（再点击/误触不移动）；长按标识弹确认框可取消；**取消标识**按钮可清除且提交随之拦截
   - [ ] 3.1.4 标题空 → 禁提交或 400 提示；优先级仅三枚举；标识大小三档切换立即生效；position 与标识一致
   - [ ] 3.1.5 提交成功/失败均有 statusText 提示（含断网、400、500）；提交成功后标识转红色留档，可继续放置下一个
   - [ ] 3.1.6 点按钮/输入框不放置标识
   - [ ] Console 无红字（尤其 `Can't add component because class 'BoxCollider' doesn't exist!`）

## 6. 常见问题

| 现象 | 排查 |
|---|---|
| 白屏/黑屏 | SceneValidator OnGUI 红字；Console `ReportPanel Inspector 引用缺失` 清单 |
| 提交提示"网络请求失败" | Base Url 是否为局域网 IP；后端是否启动；防火墙 8083 |
| 触摸无响应 | EventSystem 是否存在；是否误触 UI 防护 |
| 平面不出现 | ARPlaneManager 是否 Horizontal + Vertical；是否已授相机权限；看引导文案：30 秒内应出平面，超时提示说明需换光线充足、纹理丰富的表面 |
| 墙面无法放置标识 | 需 ARPlaneManager 勾选 **Vertical**（墙面属垂直面）；对着墙面前后缓慢扫动让 ARCore 建立垂直面 |
| 真机点击平面报 `Can't add component because class 'BoxCollider' doesn't exist!` | 已由 `Assets/link.xml` 修复（Strip Engine Code 裁掉了 CreatePrimitive 需要的 BoxCollider）；勿删除该文件，删除后重新构建会复发 |
| 真机提交 `Curl error 7: Failed to connect to localhost port 8083` | 真机上 localhost 是手机自己。USB 联调：执行 `adb reverse tcp:8083 tcp:8083` 后保持 Base Url 为 `http://localhost:8083` 即可；无线联调：手机与电脑同一 Wi-Fi，Base Url 改为电脑局域网 IP |
