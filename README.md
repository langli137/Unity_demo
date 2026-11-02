# 园区 AR 巡检问题上报 Demo

三端：**Go 后端**（`backend/`）+ **React 管理端**（`frontend/`）+ **Unity AR 移动端**（`Unity_AR_android/`）。
需求基线见 [`docs/需求文档.md`](docs/需求文档.md)；接口清单与字段契约见文末"参考"。

## 1. 使用的版本

| 部分 | 版本 |
|---|---|
| Go 后端 | Go 1.26（modernc.org/sqlite，免 CGO） |
| React 管理端 | Vite + React + TypeScript（Node.js 18+） |
| Unity 移动端 | **Unity 2022.3.62f3c1**（LTS）、AR Foundation / ARCore XR Plugin **5.1.5**、Android ARM64、IL2CPP |
| 后端端口 | 8083（`PORT` 可改）；管理端 5173 |

## 2. 三个部分如何启动

**① Go 后端**（先启动，另两端依赖它）

```bash
cd backend
go run ./cmd/server        # 默认 :8083，SQLite 存 data/issues.db，重启数据不丢
```

**② React 管理端**

```bash
cd frontend
npm install
npm run dev                # http://localhost:5173，/api 自动代理到 8083
```

**③ Unity AR 移动端**（真机 Android）

1. Unity 打开 `Unity_AR_android/`，点 **Tools → 巡检 Demo → 一键生成 AR 场景**；
2. `File → Build Settings → Android → Switch Platform`，USB 连手机点 **Build And Run**。

> 完整步骤（依赖安装、XR 配置、验收清单）见 [`Unity_AR_android/SETUP.md`](Unity_AR_android/SETUP.md)。

## 3. Unity 真机如何配置 Go 后端地址

位置：Unity 场景中选中 **Canvas → ReportPanel 组件 → Base Url** 字段。二选一：

| 方式 | 配置 | 说明 |
|---|---|---|
| **USB 联调（推荐）** | 保持默认 `http://localhost:8083`，电脑执行 `adb reverse tcp:8083 tcp:8083` | 手机上的 localhost 直通电脑后端，不依赖 Wi-Fi；拔插 USB 后需重新执行 |
| 无线联调 | 改为电脑局域网 IP，如 `http://192.168.1.50:8083` | 手机与电脑同一 Wi-Fi，防火墙放行 8083 |

> ⚠️ **手机上的 `localhost` 指手机自己，不是电脑**——不走 adb reverse 却填 localhost，提交必然连接超时（端口没配错，错的是主机名）。无线联调还需放行防火墙（管理员运行）：`netsh advfirewall firewall add rule name="AR-Inspection-Backend-8083" dir=in action=allow protocol=TCP localport=8083`；局域网 IP 变更后要同步改 Base Url。

## 4. 已完成 / 未完成 / 已知问题

**已完成**
- 后端：REST API 全套（创建/列表分页筛选/详情/改状态/字段编辑 PATCH）、三态状态机（任意互转、409 仅并发冲突）、SQLite 持久化、单测全绿
- **A1 登录与角色权限**：JWT（HS256，7 天）+ `admin`/`inspector` 双角色；除 health/login 外全部要求 `Authorization: Bearer`，删除仅 admin（inspector 返回 403）；演示账号 `admin/admin123`、`inspector/inspector123`（种子数据，生产须改密并注入 `JWT_SECRET` 环境变量）
- **A4 删除**：`DELETE /api/v1/issues/{id}`（admin）+ 管理端红字确认弹窗，级联删照片
- **B6 照片留档**：`POST/GET /api/v1/issues/{id}/photo`（raw JPEG，BLOB 存储）；AR 端提交时自动抓帧上传，管理端详情页可查看
- **B7 智能优先级建议**：后端关键词规则引擎（结构/人身安全→high，功能缺陷→medium，外观→low，未命中兜底 medium），管理端新建/编辑表单"✨ 智能建议优先级"一键采用
- **B8 导出巡检报表 CSV**：`GET /api/v1/issues/export.csv`（状态/分类/优先级/时间范围过滤，UTF-8 BOM，Excel 直开），管理端"导出报表"面板
- 管理端：问题列表（美化表格、筛选、相对时间）、详情页、状态流转、**编辑表单**、**新建补录**、错误边界、**登录页 + 顶栏身份/退出 + 401 自动回登录**
- AR 端：ARCore 平面检测（水平+垂直）、点击放置标识（**放置即锁定**，防误触移动）、标识大小三档、长按弹窗取消、**A3 提交/历史标记按优先级着色（high 红 / medium 黄 / low 蓝）**、平面检测 **30 秒超时提示**、表单上报、网络失败重试、场景一键生成、大按钮移动端布局
- **A1 AR 自动登录**：启动时用 Inspector 配置账号（默认 inspector）换 token，成功后自动填充上报人
- **A2 历史问题列表**：左上角"历史问题"面板拉取列表（状态/上报人/时间），可单条或一键按存储坐标回显优先级色标记
- **B5 断网离线暂存**：网络失败的草稿+照片落盘 `persistentDataPath/offline_queue.json`，每 10 秒自动重发（登录成功后才重发，后端明确拒绝的条目出队提示）

**未完成**
- 需求文档 D6 差异项：AR 上报人当前由自动登录身份填充（可手改），未做逐人账号管理界面
- 问题地图视图（AR 历史回显是坐标点位，非地图）、通知推送
- 跨会话/跨设备坐标对齐（历史标记用存储的世界坐标，换会话需同一空间基准才准）

**已知问题**
- **改了 ReportPanel/Placer 等序列化字段后必须重跑"一键生成 AR 场景"再打包**，否则旧场景缺引用、功能异常（真机日志会报 `Inspector 引用缺失`）。v1.4 新增的 `autoLogin/autoLoginUsername/autoLoginPassword` 有默认值，不重跑也能编译运行，但建议重跑一次
- 真机拔插 USB 后 `adb reverse` 失效，需重新执行，否则提交报 `Curl error 7: Failed to connect to localhost port 8083`
- 平面检测对环境敏感：纯白、反光、昏暗表面识别不到（30 秒后会提示换环境），非 bug
- 管理端补录的问题 position 为 `{0,0,0}` 占位（无现场 AR 坐标），AR 端回显时会跳过（无法定位）
- 本机 IP 为 DHCP 分配（如 192.168.117.140），换网络后 Base Url 需同步改

## 参考

- 接口清单 / 响应壳 / 字段契约：见 git 历史版本第 2–4 节或 [`docs/需求文档.md`](docs/需求文档.md) 第 3–4 章
- 开发过程记录：[`AI_LOG.md`](AI_LOG.md)
