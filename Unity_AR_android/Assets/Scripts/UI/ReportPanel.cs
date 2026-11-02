using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ParkInspect.AR;
using ParkInspect.Data;
using ParkInspect.Networking;

namespace ParkInspect.UI
{
    /// <summary>
    /// 上报面板（视图层，MonoBehaviour）：
    /// - 采集 标题（必填）/描述（可选）/优先级（low|medium|high 三枚举）（FR-U-04/05）；
    /// - 触摸转发给 PlaneAnchorPlacer 放置标记；点击 UI 时不放置（FR-U-08）；
    /// - v1.3 交互：放置即锁定（误触不再移动）；提交成功标记按优先级着色（A3）；
    ///   长按标记 0.8 秒弹确认框取消；同一时刻仅一个待提交标记；
    /// - 平面检测有明确时限：正常 5~20 秒，超过 DetectTimeoutSeconds（30 秒）判定超时
    ///   并给出环境建议，不再无限显示"已等待 X 秒"；
    /// - 经 IssueReportService 提交，成功/失败都显示可见提示（FR-U-07）。
    /// v1.4 新增：
    /// - A1 启动自动登录（Inspector 配置账号），登录后上报人自动填充；
    /// - B6 提交成功后上传现场截屏（raw JPEG）；
    /// - B5 网络失败的草稿+照片落盘离线队列，网络恢复后自动重发；
    /// - A2 历史问题列表（OnGUI 面板）+ 按坐标回显优先级着色标记。
    /// 分类不采集（D6：缺省 other）；后端地址在 Inspector 的 baseUrl 配置（真机填局域网 IP）。
    /// </summary>
    public class ReportPanel : MonoBehaviour
    {
        [Header("表单")]
        [SerializeField] InputField titleInput;
        [SerializeField] InputField descriptionInput;
        [SerializeField] Dropdown priorityDropdown; // 选项由 ApplyPriorityOptions 固定为 高(high)/中(medium)/低(low)
        [SerializeField] InputField reporterInput;
        [SerializeField] Dropdown markerSizeDropdown; // 标识框大小：小/中/大（0.05/0.1/0.2 米）
        [SerializeField] Button submitButton;
        [SerializeField] Button cancelButton;    // 取消标识：清除当前标记

        [Header("提示")]
        [SerializeField] Text statusText;      // 成功/失败结果（必须可见）
        [SerializeField] Text guidanceText;    // 平面检测引导文案

        [Header("AR")]
        [SerializeField] PlaneAnchorPlacer placer;

        [Header("网络")]
        [SerializeField] string baseUrl = "http://localhost:8083";

        [Header("登录（A1：自动登录 + 上报人自动填充）")]
        [SerializeField] bool autoLogin = true;
        [SerializeField] string autoLoginUsername = "inspector";
        [SerializeField] string autoLoginPassword = "inspector123";

        // 下拉选项索引 → 枚举值，与 ApplyPriorityOptions 的选项顺序一一对应（高/中/低）。
        static readonly string[] Priorities = { "high", "medium", "low" };
        // 标识框边长（米），与 ApplyMarkerSizeOptions 的选项顺序一一对应（小/中/大）。
        static readonly float[] MarkerEdges = { 0.05f, 0.1f, 0.2f };

        /// <summary>平面检测正常耗时区间（秒）：告诉用户"该等多久"，而不是一直累计秒数。</summary>
        const float NormalDetectSeconds = 20f;
        /// <summary>平面检测超时阈值（秒）：超过仍未检测到即提示超时与解决办法。</summary>
        const float DetectTimeoutSeconds = 30f;
        /// <summary>长按取消标识阈值（秒）。</summary>
        const float LongPressSeconds = 0.8f;
        /// <summary>长按命中判定：手指距标记投影的屏幕像素半径。</summary>
        const float LongPressPixelRadius = 120f;
        /// <summary>B5 离线队列重发检查间隔（秒）。</summary>
        const float OfflineRetrySeconds = 10f;

        IssueReportService service;
        ApiClient api;                 // A1/B6/B5 复用：登录、照片上传、列表拉取都走同一客户端
        OfflineQueue offlineQueue;     // B5 离线暂存队列
        Coroutine running;
        Coroutine loginRoutine;
        Coroutine flushRoutine;
        Coroutine historyRoutine;
        float detectStartTime;   // 平面检测等待起点
        float planeFoundTime = -1f; // 首次检测到平面的时刻，-1 表示尚未检测到

        // A1 登录态（登录成功后 ApiClient.Token 自动携带）。
        bool loggedIn;
        string loginDisplayName = "";
        string loginError;

        // A2 历史问题面板状态。
        bool historyOpen;
        bool historyLoading;
        string historyError = "";
        readonly System.Collections.Generic.List<IssueListItem> historyItems =
            new System.Collections.Generic.List<IssueListItem>();

        // OnGUI 弹窗/面板样式缓存（一次性创建避免每帧分配）。
        GUIStyle confirmLabelStyle;
        GUIStyle confirmButtonStyle;
        GUIStyle historyTitleStyle;
        GUIStyle historyRowStyle;
        GUIStyle historyButtonStyle;
        Vector2 historyScroll;

        // 长按取消状态机：手指按下累计时长 + 是否已弹出确认框。
        float longPressTimer;
        bool longPressArmed;      // 当前按住且位置合法
        bool confirmShown;        // 取消确认框是否弹出
        bool confirmTargetActive; // 确认框对象是否为待提交（黄）标记
        Vector2 longPressStart;
        Vector2 confirmHitPos;    // 触发长按命中时的触点位置（确认后按此位置移除）

        void Awake()
        {
            ValidateReferences();
            api = new ApiClient(baseUrl);
            service = new IssueReportService(api);
            offlineQueue = new OfflineQueue();
            if (submitButton != null)
                submitButton.onClick.AddListener(OnClickSubmit);
            if (cancelButton != null)
                cancelButton.onClick.AddListener(OnClickCancel);
            if (markerSizeDropdown != null)
                markerSizeDropdown.onValueChanged.AddListener(OnMarkerSizeChanged);
        }

        /// <summary>Inspector 接线自检：缺失引用必须可见报错，不许静默失败（FR-U-07）。</summary>
        void ValidateReferences()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (titleInput == null) missing.Add(nameof(titleInput));
            if (descriptionInput == null) missing.Add(nameof(descriptionInput));
            if (priorityDropdown == null) missing.Add(nameof(priorityDropdown));
            if (markerSizeDropdown == null) missing.Add(nameof(markerSizeDropdown));
            if (submitButton == null) missing.Add(nameof(submitButton));
            if (cancelButton == null) missing.Add(nameof(cancelButton));
            if (statusText == null) missing.Add(nameof(statusText));
            if (guidanceText == null) missing.Add(nameof(guidanceText));
            if (placer == null) missing.Add(nameof(placer));
            if (missing.Count > 0)
            {
                Debug.LogError("[ReportPanel] Inspector 引用缺失：" + string.Join(", ", missing) +
                    " —— 请按 SETUP.md 第 3 节完成场景接线（或用 Tools → 巡检 Demo → 一键生成 AR 场景）");
            }
        }

        void Start()
        {
            ApplyPriorityOptions();
            ApplyMarkerSizeOptions();
            SetStatus("", null);
            detectStartTime = Time.realtimeSinceStartup;

            int queued = offlineQueue.Count();
            if (queued > 0)
                SetStatus("检测到 " + queued + " 条离线暂存记录，网络恢复后将自动重发", Color.gray);

            // A1 自动登录：登录成功后上报人自动填充；失败不阻断上报（未登录提交会被后端 401 拒绝并可见提示）。
            if (autoLogin && loginRoutine == null)
                loginRoutine = StartCoroutine(LoginRoutine());
            // B5 离线队列定期重发（每 OfflineRetrySeconds 秒尝试一次，队列空时跳过）。
            if (flushRoutine == null)
                flushRoutine = StartCoroutine(FlushOfflineRoutine());
        }

        /// <summary>
        /// A1 自动登录协程：用 Inspector 配置的账号换 token；成功后自动填充上报人。
        /// </summary>
        IEnumerator LoginRoutine()
        {
            loginError = null;
            yield return api.Login(autoLoginUsername, autoLoginPassword, (code, message, data) =>
            {
                if (code == 0 && data != null && !string.IsNullOrEmpty(data.token))
                {
                    loggedIn = true;
                    loginDisplayName = data.user != null && !string.IsNullOrEmpty(data.user.displayName)
                        ? data.user.displayName
                        : autoLoginUsername;
                    // 上报人自动填充：仅在输入框为空时填充，不覆盖用户已填内容。
                    if (reporterInput != null && string.IsNullOrEmpty(reporterInput.text))
                        reporterInput.text = loginDisplayName;
                    SetStatus("已登录：" + loginDisplayName, Color.green);
                    Debug.Log("[ReportPanel] 自动登录成功：" + loginDisplayName);
                }
                else
                {
                    loggedIn = false;
                    loginError = string.IsNullOrEmpty(message) ? "登录失败（code=" + code + "）" : message;
                    SetStatus("自动登录失败：" + loginError + "（仍可填写表单，提交时会被拒绝）", Color.red);
                    Debug.LogWarning("[ReportPanel] 自动登录失败：" + loginError);
                }
            });
            loginRoutine = null;
        }

        /// <summary>
        /// B5 离线队列重发循环：每 OfflineRetrySeconds 秒检查一次队列，
        /// 有积压且当前空闲（无提交进行中）时逐条重发；网络仍失败则等下一轮。
        /// </summary>
        IEnumerator FlushOfflineRoutine()
        {
            var wait = new WaitForSecondsRealtime(OfflineRetrySeconds);
            while (true)
            {
                yield return wait;
                if (!loggedIn) continue;                 // 未登录：等登录成功后再重发（401 无意义）
                if (running != null || service.Submitting) continue; // 正在提交：不并发挤兑
                if (!offlineQueue.TryPeek(out OfflineQueue.Entry entry, out IssueDraft draft)) continue;

                SetStatus("网络恢复，正在重发离线暂存（剩 " + offlineQueue.Count() + " 条）…", Color.gray);
                bool done = false;
                bool ok = false;
                bool netFail = false;
                string msg = "";
                string issueId = null;
                yield return service.SubmitRoutine(draft, outcome =>
                {
                    ok = outcome.Success;
                    netFail = outcome.NetworkFailure;
                    msg = outcome.Message;
                    issueId = outcome.IssueId;
                    done = true;
                });

                if (!done) continue;
                if (ok)
                {
                    offlineQueue.Remove(entry.localId);
                    // 重发成功后补传暂存的照片（B6；无照片则跳过）。
                    if (!string.IsNullOrEmpty(entry.photoBase64))
                    {
                        byte[] photo;
                        try { photo = Convert.FromBase64String(entry.photoBase64); }
                        catch (Exception) { photo = null; }
                        if (photo != null && photo.Length > 0)
                        {
                            yield return api.UploadPhoto(issueId, photo, (c, m) =>
                            {
                                if (c != 0)
                                    Debug.LogWarning("[ReportPanel] 离线记录照片补传失败：" + m);
                            });
                        }
                    }
                    SetStatus("离线暂存已重发成功（id: " + issueId + "），队列剩 " +
                        offlineQueue.Count() + " 条", Color.green);
                }
                else if (netFail)
                {
                    // 仍是网络失败：保留队列，等下一轮。
                    SetStatus("网络仍不可用，离线暂存保留（" + offlineQueue.Count() +
                        " 条），恢复后自动重发", Color.yellow);
                }
                else
                {
                    // 后端明确拒绝（如 401/400）：该条目重发也不会成功，出队避免死循环，提示用户。
                    offlineQueue.Remove(entry.localId);
                    SetStatus("离线暂存重发被拒绝，已丢弃该条：" + msg, Color.red);
                }
            }
        }

        void Update()
        {
            UpdateGuidance();
            HandlePlacementTouch();
            HandleLongPress();
        }

        /// <summary>
        /// 触摸放置标记。放置即锁定：仅在没有待提交标记时响应点击，
        /// 因此手持误触/再次点击都不会移动已确认的位置（FR-U-08，v1.3）。
        /// </summary>
        void HandlePlacementTouch()
        {
            if (placer == null) return;
            if (confirmShown || historyOpen) return; // 弹窗/历史面板期间不响应放置

            Vector2 pos;
            bool pressBegan;
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                pos = touch.position;
                pressBegan = touch.phase == TouchPhase.Began;
            }
            else if (Input.GetMouseButtonDown(0))
            {
                pos = Input.mousePosition;
                pressBegan = true;
            }
            else return;

            if (!pressBegan) return;
            if (placer.HasMarker)
            {
                // 锁定反馈：位置不再变化，提示用长按取消。
                SetStatus("已放置标识，位置已锁定；长按标识可取消重放", Color.gray);
                return;
            }
            if (placer.TryPlace(pos, out PlaceFail fail))
            {
                // 明示放置方式：平面命中=精确；点云估算=降级，位置可能有偏差。
                SetStatus(placer.LastPlacementOnPlane
                    ? "已放置标识（黄色）；确认后提交，或长按标识取消"
                    : "平面未收敛，已按点云估算放置（位置可能有偏差）；建议移动手机等平面稳定后取消重放",
                    placer.LastPlacementOnPlane ? (Color?)Color.gray : Color.yellow);
                return;
            }

            // 放置失败不再静默（用户反馈"点了没反应"的根源）：把失败原因翻译成可见提示。
            // OverUi 除外——点在按钮/输入框上属正常 UI 交互，静默即可。
            switch (fail)
            {
                case PlaceFail.NoPlaneHit:
                    SetStatus("此处未识别到平面：请对准青色识别区域的内部再点一次（别点到区域外）", Color.yellow);
                    break;
                case PlaceFail.NoUsablePlane:
                    SetStatus("该平面还没稳定：请缓慢画弧移动手机继续扫描，再点其内部放置", Color.yellow);
                    break;
                case PlaceFail.AlreadyPlaced:
                    SetStatus("已放置标识，位置已锁定；长按标识可取消重放", Color.gray);
                    break;
            }
        }

        /// <summary>长按标记 → 弹确认框取消（v1.3：长按弹窗显示取消标识）。</summary>
        void HandleLongPress()
        {
            if (placer == null) return;

            // 确认框/历史面板弹出期间冻结长按状态。
            if (confirmShown || historyOpen) return;

            bool pressing = false;
            Vector2 pos = Vector2.zero;
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                pressing = touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved
                           || touch.phase == TouchPhase.Stationary;
                pos = touch.position;
            }
            else
            {
                // 编辑器/桌面调试路径（真机上触摸分支优先）。
                pressing = Input.GetMouseButton(0);
                pos = Input.mousePosition;
            }

            if (!pressing)
            {
                longPressTimer = 0f;
                longPressArmed = false;
                return;
            }

            // 按下起点重置计时；期间小幅移动不打断（手持抖动容忍）。
            if (!longPressArmed)
            {
                longPressArmed = true;
                longPressTimer = 0f;
                longPressStart = pos;
            }
            if ((pos - longPressStart).sqrMagnitude > LongPressPixelRadius * LongPressPixelRadius)
            {
                // 手指滑走：视作移动意图，重置长按。
                longPressTimer = 0f;
                longPressStart = pos;
                return;
            }

            longPressTimer += Time.deltaTime;
            if (longPressTimer < LongPressSeconds) return;

            // 达到长按阈值：命中检查（手指须落在某个标记附近，优先待提交标记）。
            if (placer.TryGetMarkerNear(pos, LongPressPixelRadius, out bool isActive))
            {
                confirmTargetActive = isActive;
                confirmHitPos = pos;
                confirmShown = true;
            }
            longPressTimer = 0f;
            longPressArmed = false;
        }

        /// <summary>
        /// OnGUI 弹窗/面板（无需改场景接线）：
        /// - 常驻"历史问题"入口按钮（A2）；
        /// - 历史问题列表面板：拉取列表、按坐标回显优先级着色标记；
        /// - 长按取消确认框（v1.3）。
        /// </summary>
        void OnGUI()
        {
            EnsureGuiStyles();

            // 历史入口：常驻左上角；面板打开时变为"关闭"。
            Rect toggleRect = new Rect(16, 16, Screen.width * 0.3f, 96f);
            string toggleLabel = historyOpen ? "关闭历史" : "历史问题";
            if (GUI.Button(toggleRect, toggleLabel, historyButtonStyle))
            {
                historyOpen = !historyOpen;
                if (historyOpen && historyItems.Count == 0 && !historyLoading)
                    FetchHistory();
            }

            if (historyOpen) DrawHistoryPanel();
            if (confirmShown) DrawConfirmDialog();
        }

        void EnsureGuiStyles()
        {
            if (confirmLabelStyle != null) return;
            confirmLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 46,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            confirmLabelStyle.normal.textColor = Color.white;
            // 默认 GUI.Button 字号在真机上极小（约 14px 级别），必须显式放大。
            confirmButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 44 };
            historyButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 36 };
            historyTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 38, fontStyle = FontStyle.Bold };
            historyRowStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, wordWrap = true };
        }

        /// <summary>A2 历史问题面板：列表 + 刷新 + 按坐标回显标记。</summary>
        void DrawHistoryPanel()
        {
            float panelW = Screen.width * 0.92f;
            float panelH = Screen.height * 0.62f;
            Rect panel = new Rect((Screen.width - panelW) / 2f, 130f, panelW, panelH);

            // 半透明遮罩吸收触摸（面板期间不放置/不长按）。
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Box(panel, "");

            GUI.Label(new Rect(panel.x + 20, panel.y + 14, panel.width - 240, 60),
                "历史问题（" + historyItems.Count + " 条）", historyTitleStyle);

            // 刷新按钮。
            if (GUI.Button(new Rect(panel.xMax - 210, panel.y + 12, 90, 56),
                historyLoading ? "…" : "刷新", historyButtonStyle) && !historyLoading)
            {
                FetchHistory();
            }
            // 一键回显：全部按坐标放标记。
            if (GUI.Button(new Rect(panel.xMax - 110, panel.y + 12, 90, 56), "回显", historyButtonStyle)
                && historyItems.Count > 0)
            {
                int n = ShowAllHistoryMarkers();
                SetStatus("已回显 " + n + " 枚历史标记（按优先级着色）", Color.gray);
                historyOpen = false;
            }

            float listY = panel.y + 84;
            float listH = panel.yMax - listY - 20f;
            Rect view = new Rect(panel.x + 12, listY, panel.width - 24, listH);
            Rect content = new Rect(0, 0, view.width - 30,
                historyLoading || !string.IsNullOrEmpty(historyError)
                    ? 80f
                    : historyItems.Count * 118f);

            historyScroll = GUI.BeginScrollView(view, historyScroll, content);

            float rowY = 0f;
            if (historyLoading)
            {
                GUI.Label(new Rect(10, rowY, content.width, 60), "加载中…", historyRowStyle);
            }
            else if (!string.IsNullOrEmpty(historyError))
            {
                GUI.Label(new Rect(10, rowY, content.width, 80), historyError, historyRowStyle);
            }
            else
            {
                for (int i = 0; i < historyItems.Count; i++)
                {
                    IssueListItem item = historyItems[i];
                    Rect row = new Rect(0, rowY, content.width, 110f);
                    GUI.Box(row, "");

                    // 优先级色块（A3：红/黄/蓝）。
                    Color prev = GUI.color;
                    GUI.color = PlaneAnchorPlacer.PriorityColor(item.priority);
                    GUI.DrawTexture(new Rect(row.x + 8, row.y + 12, 26, 26), Texture2D.whiteTexture);
                    GUI.color = prev;

                    string statusLabel = item.status == "resolved" ? "已解决"
                        : item.status == "in_progress" ? "处理中" : "待处理";
                    string title = string.IsNullOrEmpty(item.title) ? "（无标题）" : item.title;
                    if (title.Length > 18) title = title.Substring(0, 18) + "…";
                    GUI.Label(new Rect(row.x + 46, row.y + 8, row.width - 150, 46),
                        "[" + statusLabel + "] " + title, historyRowStyle);
                    string sub = (item.reporterName ?? "") + "  " +
                        (string.IsNullOrEmpty(item.createdAt) ? "" : item.createdAt.Substring(0, Mathf.Min(16, item.createdAt.Length)));
                    GUI.Label(new Rect(row.x + 46, row.y + 56, row.width - 150, 46), sub, historyRowStyle);

                    // 单条回显：按存储坐标放一枚优先级色标记。
                    if (GUI.Button(new Rect(row.xMax - 96, row.y + 26, 84, 56), "标记", historyButtonStyle))
                    {
                        if (ShowHistoryMarker(item))
                            SetStatus("已回显：" + item.title, Color.gray);
                        else
                            SetStatus("该问题缺少坐标，无法回显", Color.yellow);
                        historyOpen = false;
                    }
                    rowY += 118f;
                }
            }

            GUI.EndScrollView();
        }

        /// <summary>拉取历史问题列表（A2，需登录）。</summary>
        void FetchHistory()
        {
            if (historyRoutine != null) return;
            historyRoutine = StartCoroutine(FetchHistoryRoutine());
        }

        IEnumerator FetchHistoryRoutine()
        {
            historyLoading = true;
            historyError = "";

            // 未登录先登录（A1），否则列表接口 401。
            if (!loggedIn)
            {
                yield return LoginRoutine();
            }
            if (!loggedIn)
            {
                historyError = "登录失败，无法拉取历史：" +
                    (string.IsNullOrEmpty(loginError) ? "未知原因" : loginError);
                historyLoading = false;
                historyRoutine = null;
                yield break;
            }

            yield return api.GetIssues((code, message, data) =>
            {
                if (code == 0 && data != null && data.items != null)
                {
                    historyItems.Clear();
                    historyItems.AddRange(data.items);
                    historyError = "";
                }
                else
                {
                    historyError = string.IsNullOrEmpty(message)
                        ? "拉取失败（code=" + code + "）"
                        : message;
                }
            });

            historyLoading = false;
            historyRoutine = null;
        }

        /// <summary>按 issue.position 放一枚优先级着色标记（跨会话坐标系一致时位置才准确）。</summary>
        bool ShowHistoryMarker(IssueListItem item)
        {
            if (placer == null || item == null || item.position == null) return false;
            var p = item.position;
            if (p.x == 0f && p.y == 0f && p.z == 0f) return false; // 零值 = 管理端补录，无 AR 坐标
            placer.AddHistoryMarker(new Vector3(p.x, p.y, p.z), item.priority);
            return true;
        }

        /// <summary>一键回显全部有坐标的标记，返回成功放置数。</summary>
        int ShowAllHistoryMarkers()
        {
            int n = 0;
            foreach (var item in historyItems)
                if (ShowHistoryMarker(item)) n++;
            return n;
        }

        /// <summary>取消确认弹窗（长按属轻量交互）。</summary>
        void DrawConfirmDialog()
        {
            float panelW = Screen.width * 0.82f;
            float panelH = 420f;
            Rect panel = new Rect((Screen.width - panelW) / 2f, (Screen.height - panelH) / 2f, panelW, panelH);

            // 半透明遮罩：吸收触摸，防止确认期间误操作场景。
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Box(panel, "");

            string msg = confirmTargetActive
                ? "要取消这个待提交标识吗？\n取消后可重新点击平面放置"
                : "要移除这个已提交标识吗？\n移除后现场将不再显示";
            GUI.Label(new Rect(panel.x + 30, panel.y + 40, panel.width - 60, 180), msg, confirmLabelStyle);

            float btnW = (panel.width - 90) / 2f;
            float btnH = 110f;
            float btnY = panel.yMax - btnH - 40f;
            if (GUI.Button(new Rect(panel.x + 30, btnY, btnW, btnH), confirmTargetActive ? "取消标识" : "移除标识", confirmButtonStyle))
            {
                if (confirmTargetActive)
                {
                    placer.CancelMarker();
                    SetStatus("已取消标识，请重新点击平面放置", Color.gray);
                }
                else
                {
                    // 已提交标记移除：TryGetMarkerNear 已确认命中的是 submitted 集合内对象。
                    RemoveNearestSubmitted();
                    SetStatus("已移除标识", Color.gray);
                }
                confirmShown = false;
            }
            if (GUI.Button(new Rect(panel.x + 60 + btnW, btnY, btnW, btnH), "保留", confirmButtonStyle))
            {
                confirmShown = false;
            }
        }

        void RemoveNearestSubmitted()
        {
            // 弹窗期间场景输入被冻结，按触发长按时的触点位置移除命中的红色标记即可。
            placer.RemoveNearestSubmittedAt(confirmHitPos, LongPressPixelRadius);
        }

        void UpdateGuidance()
        {
            if (guidanceText == null || placer == null) return;
            if (placer.PlaneCount > 0)
            {
                if (planeFoundTime < 0f)
                {
                    planeFoundTime = Time.realtimeSinceStartup;
                    Debug.Log("[ReportPanel] 首次检测到平面，用时 " +
                        (planeFoundTime - detectStartTime).ToString("F1") + " 秒");
                }
                guidanceText.text = placer.HasMarker
                    ? "已放置标识并锁定；可填写表单提交，或长按标识取消"
                    : "已检测到平面，请点击裂缝位置放置标识";
            }
            else
            {
                // v1.3：不再无限累计"已等待 X 秒"。
                // 正常检测需 5~20 秒；超过阈值直接给超时结论与解决办法。
                float waited = Time.realtimeSinceStartup - detectStartTime;
                int raw = placer.RawPlaneCount;
                if (waited < DetectTimeoutSeconds)
                {
                    guidanceText.text = "正在检测平面，请缓慢左右移动手机\n（通常需要 " +
                        NormalDetectSeconds.ToString("F0") + " 秒内，需光线充足、纹理丰富的表面）";
                }
                else if (raw > 0)
                {
                    // 关键诊断分支：有原始碎面但一个都没收敛 → 不再误报"完全没识别到"，
                    // 告诉用户"已看到平面但不稳定"，并指引点云估算放置兜底。
                    guidanceText.text = "已检测到 " + raw + " 个初步平面但尚未稳定\n" +
                        "请继续缓慢画弧平移使其收敛；也可直接点击目标位置" +
                        "（将按点云估算放置，位置可能有偏差）";
                }
                else
                {
                    guidanceText.text = "平面检测超时（已超过 " + DetectTimeoutSeconds.ToString("F0") +
                        " 秒）\n请换到光线充足、有纹理的地面/墙面，缓慢平移重试；" +
                        "纯白/反光/昏暗表面无法识别";
                    if (planeFoundTime != -2f)
                    {
                        planeFoundTime = -2f; // 只打一次超时日志
                        Debug.LogWarning("[ReportPanel] 平面检测超时（> " + DetectTimeoutSeconds +
                            "s，原始平面数 0），已提示用户切换检测环境");
                    }
                }
            }
        }

        /// <summary>
        /// 优先级下拉固定三枚举，界面不提供其他值（FR-U-05）。
        /// 不能只看选项数量——Unity Dropdown 默认自带 3 个 "Option"，必须核对标签；
        /// 标签不符时重建，保证界面选项与提交枚举逐字对应。
        /// </summary>
        void ApplyPriorityOptions()
        {
            if (priorityDropdown == null) return;
            var opts = priorityDropdown.options;
            if (opts.Count == Priorities.Length && opts[0].text.StartsWith("高")) return;
            priorityDropdown.ClearOptions();
            priorityDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "高（high）", "中（medium）", "低（low）" });
            priorityDropdown.value = 0;
        }

        /// <summary>标识框大小下拉固定三档：小(5cm)/中(10cm)/大(20cm)，与 MarkerEdges 一一对应。</summary>
        void ApplyMarkerSizeOptions()
        {
            if (markerSizeDropdown == null) return;
            var opts = markerSizeDropdown.options;
            if (opts.Count == MarkerEdges.Length && opts[0].text.StartsWith("小")) return;
            markerSizeDropdown.ClearOptions();
            markerSizeDropdown.AddOptions(new System.Collections.Generic.List<string>
                { "小（5cm）", "中（10cm）", "大（20cm）" });
            markerSizeDropdown.value = 1; // 默认中（0.1m）
            OnMarkerSizeChanged(1);
        }

        void OnMarkerSizeChanged(int index)
        {
            if (placer == null) return;
            int idx = Mathf.Clamp(index, 0, MarkerEdges.Length - 1);
            placer.SetMarkerEdge(MarkerEdges[idx]);
        }

        void OnClickCancel()
        {
            if (placer == null || !placer.HasMarker)
            {
                SetStatus("当前没有可取消的标识", Color.gray);
                return;
            }
            placer.CancelMarker();
            SetStatus("已取消标识，请重新点击平面放置", Color.gray);
        }

        void OnClickSubmit()
        {
            if (running != null) return; // 防重复提交

            if (placer == null || !placer.HasMarker)
            {
                SetStatus("请先点击平面放置标识", Color.red);
                return;
            }

            var draft = BuildDraft();
            running = StartCoroutine(Submit(draft));
        }

        IssueDraft BuildDraft()
        {
            // 防御：Inspector 中若手动改过选项导致 value 越界，钳制到合法范围。
            int idx = priorityDropdown != null
                ? Mathf.Clamp(priorityDropdown.value, 0, Priorities.Length - 1)
                : 0;
            return new IssueDraft
            {
                title = titleInput != null ? titleInput.text : "",
                description = descriptionInput != null ? descriptionInput.text : "",
                // D6：分类不采集，保持默认 other。
                priority = Priorities[idx],
                position = new Vector3Data(placer.MarkerPosition.x, placer.MarkerPosition.y, placer.MarkerPosition.z),
                anchor = new AnchorData
                {
                    rotation = new Vector3Data(placer.MarkerRotation.x, placer.MarkerRotation.y, placer.MarkerRotation.z),
                    planeId = placer.PlaneId,
                },
                reporterName = reporterInput != null ? reporterInput.text : "",
            };
        }

        IEnumerator Submit(IssueDraft draft)
        {
            if (submitButton != null) submitButton.interactable = false;
            SetStatus("提交中…", Color.gray);

            // B6：提交前抓一帧现场画面（含已放置标识）作为留档照片；失败不阻断上报主流程。
            byte[] photoJpeg = null;
            yield return CaptureFrameJpeg(jpeg => photoJpeg = jpeg);

            // 提交前快照：成功后把该标记按优先级着色（位置已随 draft 固化）。
            IssueReportService.SubmitOutcome finalOutcome = default;
            bool finished = false;
            yield return service.SubmitRoutine(draft, outcome =>
            {
                finalOutcome = outcome;
                finished = true;
            });

            if (finished && finalOutcome.Success)
            {
                // B6：创建成功后上传现场照片；照片失败仅告警（问题本体已留档，不回滚）。
                if (photoJpeg != null && photoJpeg.Length > 0)
                {
                    SetStatus("提交成功，正在上传现场照片…", Color.gray);
                    yield return api.UploadPhoto(finalOutcome.IssueId, photoJpeg, (c, m) =>
                    {
                        if (c != 0)
                            Debug.LogWarning("[ReportPanel] 现场照片上传失败（问题已留档）：" + m);
                    });
                }

                SetStatus("提交成功（id: " + finalOutcome.IssueId + "）；标识已按优先级着色留档",
                    Color.green);
                if (titleInput != null) titleInput.text = "";
                if (descriptionInput != null) descriptionInput.text = "";
                // v1.3 业务逻辑：提交成功 = 该标识锁定为现场记录（A3：红/黄/蓝对应高/中/低），
                // 不再随手机移动/误触改变；待提交位清空，可继续放置下一个标识（一次一个）。
                // 失败则保留黄色标识便于重试。
                if (placer != null) placer.MarkSubmitted(draft.priority);
            }
            else if (finished && finalOutcome.NetworkFailure)
            {
                // B5：网络失败 → 草稿+照片落盘离线队列，网络恢复后自动重发；
                // 标识先按优先级锁定（内容已本地保存，不会丢），表单清空可继续巡查。
                offlineQueue.Enqueue(draft, photoJpeg);
                int queued = offlineQueue.Count();
                SetStatus("网络失败：" + finalOutcome.Message +
                    "\n已离线暂存（队列 " + queued + " 条），网络恢复后自动重发", Color.yellow);
                if (titleInput != null) titleInput.text = "";
                if (descriptionInput != null) descriptionInput.text = "";
                if (placer != null) placer.MarkSubmitted(draft.priority);
            }
            else
            {
                // 后端明确报错（400/401/409 等）：展示其 message，保留黄色标识与表单便于修正重试。
                string msg = finished && !string.IsNullOrEmpty(finalOutcome.Message)
                    ? finalOutcome.Message
                    : "请求被中断，请重试";
                SetStatus("提交失败：" + msg, Color.red);
            }

            running = null;
            if (submitButton != null) submitButton.interactable = true;
        }

        /// <summary>
        /// B6 抓取当前帧并编码 JPEG（quality 75，约 100~300KB）。
        /// WaitForEndOfFrame 后才能读到渲染完成的画面；任何异常都返回 null（照片是增强信息，不阻断上报）。
        /// </summary>
        IEnumerator CaptureFrameJpeg(Action<byte[]> onDone)
        {
            byte[] result = null;
            yield return new WaitForEndOfFrame();
            try
            {
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex != null)
                {
                    result = tex.EncodeToJPG(75);
                    Destroy(tex);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReportPanel] 现场截屏失败（不影响上报）：" + e.Message);
                result = null;
            }
            onDone?.Invoke(result);
        }

        void SetStatus(string msg, Color? color)
        {
            if (statusText == null) return;
            statusText.text = msg;
            statusText.color = color ?? Color.black;
        }
    }
}
