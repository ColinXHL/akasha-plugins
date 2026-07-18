# Akasha Automation 实施方案

本文将 [design.md](design.md) 细化为可按顺序执行、可测试、可验收的工作包。设计原则以 `design.md` 为准；本文负责确定代码落点、阶段依赖、交付物和质量门槛。

## 1. 当前状态与实施起点

当前仓库已完成 Phase 0、Phase 1 Companion Echo 纵向切片、Phase 2 Worker Host、Phase 3 识别/回放基础设施、Phase 4 AutoPick 与 Phase 5 AutoDialogue 纵向切片，并已进入 Phase 6 插件接入：

- `AkashaAutomation.Core` 已具备截图帧所有权、回放、坐标换算、模板匹配、OCR、窗口上下文、输入与单帧调度契约和实现。
- `AkashaAutomation.Features` 已具备 AutoPick 与 AutoDialogue 配置控制、识别/OCR/规则链、动作意图、虚拟时间等待、诊断状态和调度宿主。
- `AkashaAutomation.DevHost` 提供不依赖 AkashaNavigator 的 AutoPick/AutoDialogue 真实游戏 observe-only 测试入口，强制使用无输入观察服务。
- `AkashaAutomation.Worker` 已注册仅允许原神前台窗口的 `WindowsSendInputService`，急停在回复前同步锁死 Core Input Arbiter。
- 插件清单、启动入口和仅含“自动拾取 / 自动剧情”的 Profile 级设置界面已实现；两个功能默认关闭。
- AutoPick 与 AutoDialogue 具备测试运行时生成的确定性 1080p/1440p 回放基线；真实游戏录制帧、发行脚本和同步脚本尚未实现。
- AkashaNavigator 已实现 companion manifest、权限确认、进程管理和受限 JS API。

截至 2026-07-14，Phase 0 首批成果已经落地：

- `AkashaAutomation.BetterGiPort` 及四项目依赖边界。
- BetterGI `0.62.0` 四份默认配置、逐文件哈希和条目统计。
- 支持目录、7z/zip 的白名单导入与 `-VerifyOnly` 校验脚本。
- build/publish 资源复制、哈希篡改拒绝和依赖方向测试。
- 官方 `0.62.0` Release URL、commit、文件大小与 GitHub SHA-256 digest。

2026-07-14 已完成官方 7z 的大小、SHA-256 和选择性解压验证；四份文件与本机 `0.62.0` 取样及仓库内容逐字节一致。

同日已完成 Phase 1 Companion Echo 纵向切片：Worker 具备严格参数解析、256 KiB 长度前缀 JSON、`hello/welcome` 握手、Echo/状态/关闭请求、独立 shutdown 帧、父进程监控和断管退出；AkashaNavigator 具备固定清单校验、高风险权限确认、路径与 reparse point 校验、命名管道、单实例、Job Object、受限 JS API，以及禁用、Profile 切换、更新、卸载和应用退出清理。插件骨架与跨仓库真实进程冒烟脚本也已落地。协议契约见 `companion-protocol.md`。

2026-07-15 已完成 Phase 2：Worker 使用 .NET Generic Host 作为 composition root；生命周期由 `WorkerStateMachine` 限制合法转换；`EmergencyStopController` 在断管、父进程退出和任何关闭路径中优先锁存；收包层持续独立读取管道并以高优先级处理急停和 shutdown，因此活动自动化命令不会阻塞安全控制；有界单读命令队列在停止时取消当前命令、丢弃尚未开始的缓冲命令并拒绝新命令；并发关闭共享同一个完成任务并按“急停、命令队列、逆序资源释放、回复 shutdown”执行。`worker.getStatus` 已扩展为稳定的 Worker、游戏窗口、捕获、OCR、Feature、急停和最后错误 DTO，并加入有限保留数量的结构化 JSON 滚动日志、序列化失败降级及未观察异常处理。没有游戏窗口时 Worker 保持 `Ready`，不注册也不产生真实输入。

同日完成 Phase 3：Core 建立可释放帧/ROI、回放、识别、OCR、游戏上下文、输入和单帧调度边界；OpenCV 模板匹配、Windows Graphics Capture、PP-OCRv4 ONNX 和前台 SendInput 均有隔离实现。Worker 默认解析 `DisabledInputService`，高优先级急停在回复前同步锁死 Input Arbiter。PP-OCRv4 的最小模型集来自同一固定发行构件，并通过真实预热图推理及 Session 释放测试。

真实联调已验证：重复启动复用同一 PID、`worker.echo` 往返一致、正常停止后无运行会话。该阶段仍未引入 OpenCV、OCR 或真实输入。

因此，第一个可运行目标不是直接迁移自动剧情，而是完成一条安全的纵向链路：

```text
Akasha 插件
→ AkashaNavigator companion 管理
→ Worker 握手
→ 获取一帧
→ 产生虚拟动作意图
→ 返回状态
→ 安全停止
```

Phase 3 已通过回放、真实 PaddleOCR 预热图和 Worker 安全注册测试。Phase 4 在接入首条业务链路时仍默认只记录动作意图；人工验收前禁止注册真实键鼠输入。

2026-07-15 已完成 Phase 4：Worker 可通过协议启停和更新 AutoPick 配置，启用后由托管调度循环按单帧链路执行；识别结果进入 BetterGI 兼容规则层，再产生至多一个拾取意图并交由 Input Arbiter。默认 `DisabledInputService` 保持不变，急停会同步禁用 AutoPick 并锁死 Arbiter。

同日完成 Phase 5：调度器在 Feature 前识别 `Talk` 上下文，AutoDialogue 以高于 AutoPick 的优先级处理对话推进和选项，AutoPick 在对话帧无条件抑制。用户自定义、内置选择、高优先级暂停、橙色、默认暂停及首项/末项/随机项顺序按固定 BetterGI 源码基线迁移；黑屏、页面/道具/角色弹窗、提交物品、每日奖励/重新派遣和邀约均为独立处理器。Silero VAD 模型、许可证和进程 loopback 实现来自固定发行/源码基线；模型、音频捕获或推理不可用时使用 `IClock` 固定延迟回退。Worker 和 DevHost 仍只注册禁用/观察输入服务。

Phase 5 的真实游戏识别、OCR、规则和 VAD 已通过 observe-only 冒烟；为补足实际键鼠链路验收，新增与 DevHost/Worker 隔离的 `AkashaAutomation.LiveTestHost`。该工具仅提供 AutoPick 与 AutoDialogue 两个开关，按 BetterGI 默认 50 ms 节拍持续运行到 `Ctrl+C` 或 `Ctrl+Alt+F12`，并始终执行游戏前台检查；AutoDialogue 测试固定优先选择第一个选项。它先用于隔离验证真实输入，DevHost 继续保持 observe-only。

Phase 6 首条插件纵向链路已接入：AkashaNavigator companion 白名单允许两个 Feature 的固定协议方法；插件从当前 Profile 读取两个布尔开关，启动 Worker 后同步启用状态并记录 Worker 状态；正式 Worker 使用已经过 LiveTestHost 实机验收的前台限定输入实现。插件重载、Profile 切换、禁用和卸载继续复用 Navigator 现有的 Companion 停止链路。

Phase 6 实机反馈优化：正式 Worker、DevHost 和 LiveTestHost 改用与 BetterGI 默认一致的 BitBlt 前台客户区截图，避免 Windows Graphics Capture 的系统黄色捕获边框；Paddle OCR 在 Worker 启动后后台完成模型加载及检测/识别首推理，避免第一次出现剧情选项时才产生冷启动停顿；窗口定位结果在同一调度帧内短暂复用，去除截图前的第二次进程枚举。BetterGI 同样采用 200 ms AutoSkip 执行门限，因此保留现有动作冷却，不以提高误触风险换取表面速度。

2026-07-16 再次审计本机 BetterGI `main`，HEAD 仍为固定基线 `0eb90304`，无需跨 commit 合并。行为级复核补齐了此前翻译层遗漏的细节：AutoPick 在文字拾取动画期间抑制重复按键，文字边界提取失败时从单行识别退回完整检测识别；AutoDialogue 在 OCR 未得到文字时仍按气泡位置选择第一/最后项，没有气泡但存在对话交互键时使用交互键；剧情结束后的弹窗识别窗口保持 10 秒、提交物品窗口保持 3 秒，并恢复 BetterGI 的道具弹窗 1 秒、黑屏与邀约 1.2 秒独立节流。滚轮翻动拾取列表仍按既定本地补丁延后，不把会改变玩家滚轮状态的副作用纳入两个简单开关。

## 2. 已确定的实现决策

### 2.1 项目依赖

新增 `AkashaAutomation.BetterGiPort` 项目，用于保存上游形状和兼容层：

```text
AkashaAutomation.Core
        ↑
AkashaAutomation.BetterGiPort
        ↑
AkashaAutomation.Features
        ↑
AkashaAutomation.Worker
```

允许 `Features` 同时直接引用 `Core`，但禁止：

- Core 引用 BetterGiPort、Features 或 Worker。
- BetterGiPort 引用 Features 或 Worker。
- Features 管理命名管道、父进程或 Worker 生命周期。
- Worker 包含自动拾取和自动剧情识别规则。

### 2.2 BetterGI 源码与资源基线

源码初始基线：

```text
repository: https://github.com/babalae/better-genshin-impact.git
branch: origin/main
commit: 0eb90304c4e4fa1f5cee2a4cbf68de6c8200ec94
version: 0.62.1-alpha.2
```

已核验的本机发行资源来自 `C:\Program Files\BetterGI`，EXE 产品版本为 `0.62.0`。该目录只用于核验和首次取样，不作为 CI 输入。

已核验的小型资源：

| 资源 | SHA-256 |
|---|---|
| `Assets/Config/Pick/default_pick_black_lists.json` | `1129650653EED1EC7E81676B3F616895FEB9433AB616EFC98AC360232C7E7EA9` |
| `Assets/Config/Skip/default_pause_options.json` | `212962F57E0BB0C04D9C3AF062BE53DDD929573F0399BC29B4476EC646F2EF65` |
| `Assets/Config/Skip/pause_options.json` | `FCC7D1E985862F0E3B0CC59CAD7312642F7E96A318A73FC7646C093701A08B5B` |
| `Assets/Config/Skip/select_options.json` | `8585CA3368566A6EFE15EF52A816494AC2469470D7AC3B806D3D329CB4B36E88` |

正式导入前必须补齐以下证据：

1. 可重复下载的 BetterGI Release 或 alpha 构件 URL。
2. 完整构件 SHA-256。
3. 从构件选择性解压上述文件后，逐文件哈希与本机取样一致，或记录差异原因。
4. PP-OCRv4 与 Silero VAD 模型的来源、许可证、路径和哈希已完成；Yap 若在未来功能中启用仍须单独补齐。

源码 commit 与运行资源使用两个独立 pin。选择源码提交时必须检查它是否新增、删除或改变运行资源要求；不默认假设相同版本号即可兼容。

### 2.3 上游代码形状

BetterGiPort 内的上游代码遵循以下规则：

- 保留可识别的上游目录、文件名、类名和方法顺序。
- 不做与移植无关的格式化、命名修改或方法拆分。
- 本地变更优先进入 `Compatibility` 和 Akasha Adapter。
- 必须修改上游文件时，用小提交记录原因，并在同步清单登记。
- `AutoPickTrigger`、`AutoSkipTrigger` 等名称可以留在 Port 内；对外只暴露 `AutoPickFeature`、`AutoDialogueFeature`。

### 2.4 输入安全

真实输入必须满足全部条件：

```text
握手成功
∧ 父进程仍存活
∧ 管道仍连接
∧ 全局自动化已启用
∧ Feature 已启用
∧ GameContext 确认目标游戏窗口
∧ 不处于 emergency stop
```

任意条件失效时，Input Arbiter 立即拒绝新动作并释放已按下按键。单元测试和回放测试永远只注册 `RecordingInputService`。

首个可用版本只启用前台输入。BetterGI 的后台 `PostMessage` 操作作为独立能力验收，不与基础迁移同时开放。

### 2.5 通信帧格式

命名管道采用长度前缀 UTF-8 JSON：

```text
4 字节 little-endian payload length
N 字节 UTF-8 JSON payload
```

首版限制单条消息不超过 256 KiB，不传输截图、OpenCV Mat 或模型数据。消息类型：

```text
hello
welcome
request
response
event
shutdown
```

请求与响应必须包含 correlation ID。首条 Worker 消息必须携带协议版本、一次性令牌、Worker 版本和父进程 PID。

## 3. 目标目录

```text
src/
├─ AkashaAutomation.Core/
│  ├─ Abstractions/
│  ├─ Capture/
│  ├─ Recognition/
│  ├─ Ocr/
│  ├─ Input/
│  ├─ GameContext/
│  ├─ Scheduling/
│  └─ Diagnostics/
├─ AkashaAutomation.BetterGiPort/
│  ├─ Upstream/
│  │  ├─ AutoPick/
│  │  └─ AutoSkip/
│  ├─ Compatibility/
│  └─ Assets/
│     ├─ Config/Pick/
│     ├─ Config/Skip/
│     ├─ Recognition/
│     └─ Model/
├─ AkashaAutomation.Features/
│  ├─ AutoPick/
│  └─ AutoDialogue/
└─ AkashaAutomation.Worker/
   ├─ Bridge/
   ├─ Hosting/
   ├─ Configuration/
   └─ Logging/
tests/
├─ AkashaAutomation.Core.Tests/
├─ AkashaAutomation.BetterGiPort.Tests/
├─ AkashaAutomation.Features.Tests/
└─ AkashaAutomation.Worker.IntegrationTests/
upstream/
└─ bettergi/
   ├─ manifest.json
   ├─ hashes.json
   └─ reports/
scripts/
├─ Import-BetterGiAssets.ps1
├─ Inspect-BetterGiUpdate.ps1
├─ Publish-Plugin.ps1
└─ Verify-Package.ps1
```

## 4. Phase 0：来源、资源和工程边界

### 目标

在复制业务代码前建立可追溯来源、稳定依赖方向和可重复资源导入。

### 工作项

1. 新建 `AkashaAutomation.BetterGiPort` 并加入解决方案。
2. 更新项目依赖测试，固定 Core、Port、Features、Worker 的引用方向。
3. 新建 `upstream/bettergi/manifest.json`，记录源码 pin、发行构件 pin、源到目标路径映射和本地补丁。
4. 新建 `upstream/bettergi/hashes.json`，记录所有配置、PNG、字典和模型哈希。
5. 实现 `Import-BetterGiAssets.ps1`：
   - 输入本地 BetterGI `.7z` 或已解包目录。
   - 只允许 manifest 声明的路径。
   - 阻止目录穿越和符号链接逃逸。
   - 验证 SHA-256 和 JSON 可解析性。
   - 输出新增、删除、修改和重复条目统计。
6. 首次导入四份配置文件，保留 BetterGI 原相对路径。
7. 扩展 `DERIVATION.md` 与 `THIRD_PARTY_NOTICES.md`。

### 交付物

- 可构建的四项目解决方案。
- 可重复运行的资源导入命令。
- 默认拾取黑名单和剧情关键词进入仓库并随 publish 复制。
- 来源和哈希清单完整。

### 退出门槛

- `dotnet build` 和 `dotnet test` 通过。
- 连续运行两次资源导入，第二次工作树无变化。
- 篡改任一资源后，校验脚本必然失败。
- 默认拾取黑名单可解析，条目数和唯一条目数有测试断言。

## 5. Phase 1：Companion Echo 纵向切片

该阶段同时涉及 `AkashaNavigator` 与本仓库，是后续所有 Worker 功能的前置条件。

### AkashaNavigator 工作项

1. 扩展 plugin manifest，加入固定的 `companion` 声明。
2. 增加高风险 `companion` 权限及安装/首次启用确认。
3. 实现 `ICompanionProcessManager`：
   - 规范化并验证插件根目录内的 EXE 路径。
   - 拒绝 reparse point、junction、符号链接和目录穿越逃逸。
   - `UseShellExecute = false`。
   - 每插件单实例。
   - 保存真实进程句柄，不按进程名管理。
4. 建立随机管道名和一次性会话令牌。
5. 将 Worker 放入带 `KILL_ON_JOB_CLOSE` 的 Job Object。
6. 暴露受限 JS API：`start`、`invoke`、`getStatus`、`stop`。
7. 在插件禁用、Profile 切换、更新、卸载和应用退出时停止 Worker。

### Worker 工作项

1. 解析 `--pipe`、`--token`、`--parent-pid`、`--protocol-version`。
2. 参数不完整、PID 非法或协议版本不支持时，在创建任何输入服务前退出。
3. 连接命名管道并完成 token 握手。
4. 实现 `worker.getStatus`、`worker.shutdown` 和 Echo 调用。
5. 监视父进程和管道断开。
6. 日志写入当前用户的 LocalApplicationData，不写插件安装目录。

### 退出门槛

- 合法握手成功，非法 token 和协议版本被拒绝。
- 重复启动只存在一个 Worker。
- 正常 stop、插件禁用、Profile 切换和更新前清理无残留进程。
- Akasha 强制退出后 Worker 自动结束。
- 该阶段不引用 OpenCV、OCR 或真实输入实现。

## 6. Phase 2：Worker Host 与安全生命周期

状态：已完成（2026-07-15）。

### 工作项

1. 引入 Generic Host 作为 Worker composition root。
2. 实现 `WorkerStateMachine`：

```text
Created
→ Connecting
→ Handshaking
→ Ready
→ Running
→ Stopping
→ Stopped
```

3. 实现 `EmergencyStopController`，其状态优先于配置和 Feature。
4. 实现有界命令队列，停止阶段拒绝新请求。
5. 定义稳定的状态 DTO：Worker、游戏窗口、捕获、OCR、Feature、最后错误。
6. 实现结构化日志和有限大小的滚动文件。
7. 正确处理取消、未观察异常和资源释放顺序。

### 退出门槛

- 状态机非法转换有单元测试。
- 管道断开先触发 emergency stop，再释放其他资源。
- 并发 shutdown 只执行一次且所有调用者获得确定结果。
- Worker 没有游戏窗口时保持 Ready，不崩溃、不产生输入。

补充安全语义：Navigator 使用短期写锁和 correlation ID 响应分派，急停不会等待普通请求完成；急停与 shutdown 在 Worker 收包层优先于普通命令队列处理；急停取消活动自动化命令，关闭阶段不执行尚未开始的缓冲命令；普通命令使用非阻塞有界 admission，队列已满时立即返回 `queue_full`；`worker.shutdown` 仅在资源释放完成后回复成功。

## 7. Phase 3：Core、识别与帧回放

状态：已完成（2026-07-15）。

已落地内容包括：明确所有权的 `CapturedFrame`/ROI、1080p/1440p 坐标换算、文件回放、OpenCV 模板匹配、PP-OCRv4 ONNX 识别、Windows Graphics Capture、隔离的前台 `SendInput`、虚拟时间、诊断记录、Recording/Disabled Input、单帧调度和急停优先的 Input Arbiter。最小 PaddleOCR V4 模型集从固定 BetterGI `0.62.0` 发行包白名单提取并纳入逐文件哈希清单。

### Core 接口

优先定义以下最小接口，不提前复制 BetterGI 静态上下文：

```text
ICaptureSource
IGameWindowLocator
IGameContextProvider
ITemplateMatcher
IOcrEngine
IInputService
IInputArbiter
IClock
IAssetPathResolver
IDiagnosticsSink
```

### 核心模型

```text
CapturedFrame
CaptureSize
RegionOfInterest
RecognitionResult
OcrResult
GameContextSnapshot
AutomationIntent
InputActionGroup
FeatureDecision
```

所有图像对象必须明确所有权：创建方负责释放，派生 ROI 不得在父图像释放后使用。针对 `Mat`、OCR Session 和原生句柄编写生命周期测试。

### 实现顺序

1. `FakeClock`、`RecordingInputService`、`InMemoryDiagnosticsSink`。
2. 帧文件 `ReplayCaptureSource`。
3. ROI、缩放和坐标变换。
4. OpenCV 模板匹配。
5. Paddle OCR；Yap 作为 AutoPick 后续兼容项。
6. Windows Graphics Capture 前台截图实现。
7. 前台 `SendInput` 实现，但默认不在 Host 注册。
8. 单帧调度器和 Input Arbiter。

### 退出门槛

- 1080p 和 1440p 坐标换算测试通过。
- 回放模式无法解析或调用真实输入服务。
- 同一帧多个 Feature 意图最终至多执行一组动作。
- emergency stop 后所有动作意图被拒绝。
- 连续回放不会增长未释放的 Mat、OCR Session 或句柄数量。

## 8. Phase 4：AutoPick 首条业务纵向切片

> 状态：已于 2026-07-15 完成。已从固定 BetterGI `0.62.0` 发行包白名单导入 E/F/G/L、对话和设置图标模板，并按固定 `0.62.1-alpha.2` 源码 commit 迁移 OCR 清洗、硬编码排除和名单优先级。Worker 新增 get/set options、setEnabled、最近识别状态及按启用状态运行的调度宿主；输入仍固定为 `DisabledInputService`。1080p/1440p 正负回放及规则、配置、急停测试均已覆盖。Profile 文件持久化仍按原计划归 Phase 6 插件体验实现。

> 实机测试补充：增加独立 `AkashaAutomation.DevHost` 控制台入口，直接复用真实窗口发现、WGC、PaddleOCR、AutoPick Feature 和 Input Arbiter。它不引用 Worker 或 AkashaNavigator，不包含 `WindowsSendInputService`，只打印 `text`、`reason`、`wouldPress` 和仲裁结果。

AutoPick 比 AutoDialogue 范围小，先用于验证完整的截图、识别、OCR、规则、动作意图和配置链路。

### 行为范围

按 BetterGI 当前优先级迁移：

```text
交互键模板
→ 特殊 L 键排除
→ 对话/设置图标排除
→ 文字 ROI 与 OCR
→ OCR 清洗
→ 硬编码 DoNotPick
→ 长度检查
→ 精确白名单
→ 排除图标
→ 精确黑名单
→ 模糊黑名单
→ PickIntent
```

### 配置模型

```text
Enabled
PickKey
OcrEngine
BlackListEnabled
WhiteListEnabled
UserExactBlacklist
UserFuzzyBlacklist
UserWhitelist
ItemIconLeftOffset
ItemTextLeftOffset
ItemTextRightOffset
```

默认名单是包内只读资源；用户名单存储在 Akasha Profile 的插件配置目录。插件更新不得覆盖用户名单。

### 测试矩阵

- 普通拾取物：产生 PickIntent。
- NPC 对话图标：默认不产生动作。
- 设置/机关图标：默认不产生动作。
- 白名单精确命中：可以覆盖图标排除。
- 硬编码 `DoNotPick`：优先于白名单。
- 精确黑名单、模糊黑名单分别命中。
- OCR 空、单字符和噪声：不产生动作。
- 黑白名单关闭：普通交互直接产生意图，排除图标仍不触发。
- 自定义交互键模板与键码一致。
- 禁用 Feature 和 emergency stop：不产生动作。

### 退出门槛

- 全部规则测试通过。
- 至少有 1080p、1440p 各一组正例和负例回放。
- 默认黑名单、用户名单合并顺序与 BetterGI 一致。
- Worker 状态能报告最近识别文字、决策原因和是否提交动作。
- 人工启用真实输入前，先在日志模式运行并确认动作意图位置。

## 9. Phase 5：AutoDialogue 迁移

> 状态：已于 2026-07-15 完成。Worker 支持 AutoDialogue get/set options 与 setEnabled，状态报告包含 Talk 分类、选项文本、决策、意图和 VAD/回退状态。DevHost 新增 `--feature auto-dialogue` 独立 observe-only 实机入口。所有等待均由 `IClock` 或单帧状态机驱动；Worker shutdown 在回复前停止调度、锁死输入并释放 loopback、截图、OCR 和模板资源。

### 5.1 基础对话推进

- 识别 Talk GameContext。
- 按配置产生空格或交互键意图。
- 使用 `IClock` 和调度状态替代 `Thread.Sleep`。
- 对话进行时抑制 AutoPick。

### 5.2 选项识别与优先级

迁移以下顺序并独立测试：

```text
用户自定义优先选项
→ 内置 select options
→ 高优先级 pause options
→ 橙色选项
→ default pause options
→ 第一项 / 最后一项 / 随机项
```

模板识别负责定位气泡、感叹号和邀约选项；OCR 负责读取文本；颜色识别负责橙色选项。

### 5.3 特殊场景

- 剧情黑屏点击。
- 书页和道具弹窗关闭。
- 新角色介绍弹窗关闭。
- 提交物品。
- 每日委托奖励与重新派遣。
- 邀约分支和跳过按钮。

每个场景必须是独立处理器，输出动作意图，不在识别方法中直接调用输入。

### 5.4 Silero VAD

作为独立子阶段实现：

1. 验证 `Assets/Model/Vad/silero_vad.onnx` 来源与许可证。
2. 捕获指定游戏进程的 loopback 音频。
3. 通过 VAD 判断语音开始和结束。
4. 超时、模型缺失、捕获失败时回退到固定延迟。
5. Feature 禁用、管道断开和 shutdown 时立即释放音频资源。

### 退出门槛

- 选项优先级与 BetterGI 基线回放一致。
- 每个特殊场景至少一个正例和一个相似负例。
- VAD 不可用时自动剧情仍可工作。
- 对话激活期间 AutoPick 不争抢输入。
- 所有等待可由虚拟时间驱动，测试中无真实 sleep。

## 10. Phase 6：插件体验

### 插件职责

- onLoad 启动 companion 并读取状态。
- 设置 AutoPick 和 AutoDialogue options。
- Profile 级保存用户名单和 Feature 配置。
- 显示 Worker、游戏窗口、OCR、Feature 和错误状态。
- 提供 Feature 热键与全局 emergency stop。
- companion 权限未授予时给出明确说明。

### 面板最小内容

```text
Worker 状态
游戏窗口状态
自动拾取开关与名单编辑
自动剧情开关与选项策略
最近识别/动作诊断
紧急停止
日志目录入口
```

插件只调用固定 companion 方法；不得传入 EXE 路径、工作目录、命令行或环境变量。

### 退出门槛

- Profile 切换能停止旧会话并加载新配置。
- 设置变更有 schema 校验，非法值不会到达 Worker。
- emergency stop 无论面板是否打开都可触发。
- Worker 不存在或崩溃时面板不会卡死，并可受控重启。

## 11. Phase 7：同步、打包与发行

### BetterGI 同步命令

`Inspect-BetterGiUpdate.ps1` 输入旧、新 commit，输出：

- 路径过滤后的提交列表。
- 每个提交的文件增删改。
- AutoPick、AutoSkip、Recognition、Capture、OCR、Input 分类。
- 新增 NuGet、原生 DLL、配置、PNG、字典和模型要求。
- 与本地 Port 文件的对应关系。

每个候选提交标记为：

```text
adopt      原样或近似移植
translate  只移植行为，不引入上游基础设施重构
defer      当前版本暂不接入
ignore     与本项目范围无关
```

### LLM 使用边界

LLM 可以：

- 总结上游 commit 和 PR 的行为变化。
- 识别依赖和资源变化。
- 生成候选 Port 补丁、Adapter 修改和回放用例。
- 更新 manifest、DERIVATION 和同步报告草案。

LLM 不可以单独决定：

- 自动合并真实输入和线程生命周期变化。
- 删除或替换模板、字典和模型。
- 改变 ROI、阈值、动作优先级而没有回放证据。
- 在缺少来源与哈希时加入发行资源。

### 插件打包

`Publish-Plugin.ps1` 执行：

```text
dotnet test
→ dotnet publish Worker
→ 复制托管和原生依赖
→ 复制 Config / Recognition / Model 资源
→ 验证 plugin.json
→ 验证来源和文件哈希
→ 检查没有用户配置进入包
→ 生成 package-manifest.json
→ 打包 ZIP
→ 解压到临时目录再次验证
```

### 退出门槛

- 全新目录安装后不需要 BetterGI 即可启动。
- 删除或修改任一必需资源后包验证失败。
- 更新和卸载不会被 Worker 文件句柄阻塞。
- ZIP 不包含测试帧、用户名单、日志或开发机绝对路径。
- LICENSE、DERIVATION、THIRD_PARTY_NOTICES 随包分发。

## 12. 测试门禁

| 门禁 | 每次提交 | Feature 合并 | Release |
|---|---:|---:|---:|
| 编译与依赖方向测试 | 是 | 是 | 是 |
| Core/Feature 单元测试 | 是 | 是 | 是 |
| 相关帧回放 | 相关时 | 是 | 全量 |
| Worker 协议集成测试 | Bridge 相关时 | 是 | 全量 |
| 父进程/断管生命周期测试 | Hosting 相关时 | 是 | 全量 |
| 资源哈希与来源验证 | 资源相关时 | 是 | 是 |
| 插件解包后自检 | 否 | 是 | 是 |
| 真实游戏冒烟 | 否 | 候选版本 | 是 |

真实游戏冒烟必须从只记录动作意图开始，再显式开启真实输入。测试配置不得默认开启后台输入。

## 13. 推荐提交顺序

为降低一次性迁移风险，按以下小批次提交：

1. `docs`: 同步策略和本实施方案。
2. `build`: 增加 BetterGiPort 项目和依赖方向测试。
3. `vendor`: 导入四份配置、manifest、hashes 和资源校验测试。
4. `bridge`: 定义协议 DTO、编码器和 Worker 参数解析。
5. `navigator`: 实现 companion Echo 纵向切片。
6. `hosting`: Worker 生命周期、父进程监控和 emergency stop。
7. `core`: 虚拟时间、动作意图、RecordingInputService 和调度器。
8. `recognition`: 帧回放、ROI、模板匹配和 Paddle OCR。
9. `autopick`: 迁移 AutoPick 并完成回放。
10. `autodialogue`: 分场景迁移 AutoDialogue。
11. `audio`: 独立接入 Silero VAD。
12. `plugin`: 面板、配置、热键和状态。
13. `release`: 打包、自检、来源和选择性同步流水线。

任何提交都不得同时进行上游行为迁移和本地算法优化。

## 14. 已完成的首个执行批次

以下 Phase 0 首个执行批次已经完成；保留本节作为历史实施记录：

1. 创建 `AkashaAutomation.BetterGiPort`。
2. 更新解决方案和依赖方向测试。
3. 创建 manifest 与 hashes schema。
4. 从固定输入目录导入四份小型配置。
5. 配置 build/publish 复制规则。
6. 添加 JSON、条目统计、哈希和重复导入测试。
7. 完成许可证与来源记录。

该批次完成后已继续完成跨仓库 Companion Echo 与 Phase 2 Worker Host。
