# Akasha Automation 设计方案

## 1. 项目定位

Akasha Automation 是 AkashaNavigator 的进程外游戏自动化组件。用户只安装 Akasha 插件，插件通过 AkashaNavigator 启动随包分发的 `AkashaAutomation.Worker.exe`，不要求安装、运行或配置 BetterGI。

首批功能：

- 自动拾取。
- 自动剧情。

自动拾取和自动剧情以固定 BetterGI 源码 commit 与发行资源为初始来源。移植代码通过独立 Port 层与 Akasha 运行时隔离；后续只在明确的版本窗口选择性同步相关修复、识别规则和资源，不持续镜像 BetterGI `main`，也不要求用户安装 BetterGI。

## 2. 设计目标

1. 用户只需要安装一个 Akasha 插件包。
2. AkashaNavigator 不引入 OpenCV、OCR、截图或自动输入依赖。
3. Worker 不依赖 BetterGI 的安装目录、配置目录或运行进程。
4. 插件不能借助 companion 权限启动任意程序或执行任意命令。
5. 插件禁用、Profile 切换、更新、卸载或 Akasha 异常退出时，Worker 都能安全停止。
6. 截图识别与输入可以通过帧回放测试验证，不依赖纯人工游戏测试。
7. 剥离期间优先保持行为不变；通过回放验证后，再进行 Akasha 命名、依赖注入和模块重构。

## 3. 非目标

- 不自动追随或无审查合并 BetterGI 上游的全部功能与修复。
- 不保留 BetterGI UI、ViewModel、设置窗口和托盘功能。
- 不提供通用脚本执行、Shell 或任意进程启动权限。
- 不在 AkashaNavigator 进程内加载 OpenCV、OCR 或 BetterGI 衍生程序集。
- 第一阶段不优化已经稳定的自动拾取和自动剧情算法。

## 4. 仓库与许可证边界

系统拆分为两个独立仓库：

### AkashaNavigator

负责：

- 插件清单中的 `companion` 声明。
- companion 权限确认。
- Worker 路径验证、启动、停止和进程监管。
- JS 与 Worker 之间的消息转发。
- 插件更新和卸载前的 Worker 清理。

AkashaNavigator 不引用 Akasha Automation 的任何程序集。

### AkashaPlugins 中的自动化插件

负责：

- Worker 可执行程序。
- 截图、识别、OCR、输入和调度运行时。
- 自动拾取与自动剧情。
- Akasha 插件源码及最终插件包。
- 帧回放和进程集成测试。

直接提取的 BetterGI 代码及其衍生修改按 GPL-3.0 管理。插件目录保留来源、许可证、版权和第三方组件说明。进程拆分保持宿主与 Worker 的工程边界。

## 5. 运行架构

```text
AkashaNavigator
├─ PluginHost
├─ CompanionApi
└─ CompanionProcessManager
       │
       │ 启动并监管
       ▼
AkashaAutomation.Worker
├─ Companion Bridge
├─ Game Context
├─ Capture / OCR / Recognition
├─ Automation Scheduler
├─ AutoDialogue
├─ AutoPick
└─ Input Arbiter
```

开发与实机识别验收另提供 `AkashaAutomation.DevHost`。它直接组合 Core、BetterGiPort 与 Features，不连接 companion 协议，不进入插件发行包，并且只允许 observe-only 输入服务。

运行流程：

```text
插件 onLoad
→ companion.start()
→ Akasha 校验 manifest 和 Worker 路径
→ Akasha 创建随机命名管道和会话令牌
→ 启动 Worker
→ 完成协议握手
→ 插件读取状态并启用配置中的功能
→ Worker 查找游戏窗口并启动调度循环
```

## 6. 项目结构

```text
plugins/akasha-genshin-automation/
├─ manifest.json
├─ README.md
├─ LICENSE
├─ DERIVATION.md
├─ THIRD_PARTY_NOTICES.md
├─ frontend/
│  ├─ main.js
│  └─ settings_ui.json
├─ packaging/
│  └─ Publish-Plugin.ps1
└─ backend/
├─ AkashaAutomation.sln
├─ Directory.Build.props
├─ global.json
├─ docs/
│  └─ design.md
├─ src/
│  ├─ AkashaAutomation.Core/
│  │  ├─ Abstractions/
│  │  ├─ Capture/
│  │  ├─ Recognition/
│  │  ├─ Ocr/
│  │  ├─ Input/
│  │  ├─ GameContext/
│  │  ├─ Scheduling/
│  │  └─ Diagnostics/
│  ├─ AkashaAutomation.BetterGiPort/
│  │  ├─ Upstream/
│  │  │  ├─ AutoPick/
│  │  │  └─ AutoSkip/
│  │  ├─ Compatibility/
│  │  └─ Assets/
│  ├─ AkashaAutomation.Features/
│  │  ├─ AutoPick/
│  │  │  ├─ Recognition/
│  │  │  └─ Assets/
│  │  └─ AutoDialogue/
│  │     ├─ Recognition/
│  │     ├─ Models/
│  │     └─ Assets/
│  ├─ AkashaAutomation.DevHost/
│  │  ├─ Program.cs
│  │  └─ AutoPickDevHost.cs
│  └─ AkashaAutomation.Worker/
│     ├─ Bridge/
│     ├─ Hosting/
│     ├─ Configuration/
│     └─ Logging/
├─ upstream/
│  └─ bettergi/
│     ├─ manifest.json
│     └─ hashes.json
├─ tests/
│  ├─ AkashaAutomation.Core.Tests/
│  ├─ AkashaAutomation.BetterGiPort.Tests/
│  ├─ AkashaAutomation.Features.Tests/
│  ├─ AkashaAutomation.Worker.IntegrationTests/
│  └─ TestInfrastructure/
├─ testdata/
│  └─ frames/
│     ├─ auto-pick/
│     └─ auto-dialogue/
└─ scripts/
```

### AkashaAutomation.Core

包含所有功能共享的运行时能力，不引用 `Features` 或 `Worker`：

- 游戏窗口发现与上下文。
- 截图生命周期。
- 图像区域与识别基础类型。
- OCR 引擎抽象和实现。
- 键鼠输入抽象和实现。
- 自动化调度器。
- 日志与诊断接口。

需要优先形成的测试接缝：

```text
ICaptureSource
IOcrEngine
IInputService
IClock
IDiagnosticsSink
```

### AkashaAutomation.Features

只包含面向用户的自动化行为：

- `AutoPickFeature`。
- `AutoDialogueFeature`。
- 对应配置、识别规则、模型和素材。

Feature 只通过 Core 接口和 BetterGI Port 适配器获取截图、OCR、上下文和输入能力，不直接管理进程、命名管道或应用生命周期。

### AkashaAutomation.BetterGiPort

包含需要保留上游对应关系的 BetterGI 衍生代码与素材，是同步边界而不是运行时进程边界：

- 尽量保留上游文件划分、类名、方法顺序和行为，避免无必要的格式化与重构。
- 通过 `Compatibility` 将 BetterGI 的静态上下文、素材路径、日志、截图和输入调用映射到 Core 接口。
- 不迁移 WPF UI、ViewModel、托盘、更新器、脚本系统和无关任务。
- 默认黑名单、剧情关键词、模板和必需模型随仓库与插件包分发，运行时不读取 BetterGI 安装目录。
- `Features` 对外提供 `AutoPickFeature` 与 `AutoDialogueFeature`，上游名称不泄漏到 companion 协议。

依赖方向固定为：

```text
Core ← BetterGiPort ← Features ← Worker
  ↑____________________|
```

### AkashaAutomation.Worker

作为 Composition Root 负责：

- 解析宿主生成的启动参数。
- 建立 companion 会话。
- 注册 Core 与 Features。
- 维护游戏和自动化生命周期。
- 处理状态查询、配置更新和紧急停止。
- 在管道断开或父进程退出时停止输入并退出。

Worker 使用 `WinExe`，不创建控制台窗口。首版按 `win-x64`、framework-dependent、非 single-file 发布，以避免 OpenCV、OCR 模型及原生 DLL 的单文件提取问题。Akasha 安装程序已经提供 .NET 8 Desktop Runtime 和 VC++ Runtime。

### AkashaAutomation.DevHost

仅用于开发阶段独立验证真实游戏窗口、截图、OCR 与 Feature 决策：

- 不引用 AkashaNavigator 或 Worker companion 生命周期。
- 不接受管道、令牌、父进程或真实输入参数。
- 固定使用 observe-only 输入服务，在控制台输出本应提交的动作。
- 发布目录与正式插件包隔离，避免把开发入口作为用户功能分发。

## 7. Companion 清单和 API

建议的插件清单：

```json
{
  "permissions": ["companion", "panel", "hotkey", "events"],
  "companion": {
    "entry": "runtime/AkashaAutomation.Worker.exe",
    "protocolVersion": 1,
    "lifetime": "plugin",
    "singleInstance": true
  }
}
```

插件不能在运行时指定可执行文件、工作目录、命令行参数、Shell 命令或环境变量。

JS API：

```javascript
companion.start();
companion.invoke("features.autoPick.setEnabled", { enabled: true });
companion.invoke("features.autoDialogue.setEnabled", { enabled: true });
companion.getStatus();
companion.stop();
```

## 8. 通信协议

AkashaNavigator 与 Worker 使用命名管道，JS 不直接访问管道。`companion.invoke()` 由 Akasha 转发 JSON 消息。

Akasha 自动生成启动参数：

```text
--pipe <随机管道名>
--token <一次性会话令牌>
--parent-pid <Akasha PID>
--protocol-version 1
```

协议特征：

- 请求/响应包含关联 ID。
- 首次消息必须完成协议版本和令牌握手。
- 管道 ACL 仅允许当前 Windows 用户。
- JS 不获得管道名和令牌。
- 状态 DTO 只交换稳定的业务数据，不交换内部对象或图像矩阵。
- 不监听固定 localhost 端口。

首版命令范围：

```text
worker.getStatus
worker.shutdown
features.autoPick.getOptions
features.autoPick.setOptions
features.autoPick.setEnabled
features.autoDialogue.getOptions
features.autoDialogue.setOptions
features.autoDialogue.setEnabled
automation.emergencyStop
```

## 9. 进程生命周期与安全

AkashaNavigator 中增加单例 `ICompanionProcessManager`，按插件 ID 持有真实进程句柄和会话。

启动限制：

- EXE 必须位于插件根目录下。
- 使用规范化绝对路径检查目录穿越。
- 拒绝穿过 reparse point、junction 或符号链接逃逸插件目录。
- 使用 `UseShellExecute = false`。
- 一个插件在一个 Akasha 实例中只能拥有一个 Worker。
- companion 是高风险权限，安装或首次启用时必须明确展示。

退出顺序：

```text
停止接受新的 Feature 命令
→ 禁止新的输入动作
→ 取消当前自动化动作并等待其响应取消
→ 释放截图和 OCR
→ 回复 shutdown
→ Worker 退出
→ 超时后由 Akasha 终止进程树
```

异常保障：

- Worker 监视父进程 PID。
- Worker 在管道断开后立即禁用输入。
- Akasha 将 Worker 加入带 `KILL_ON_JOB_CLOSE` 的 Windows Job Object。
- 更新和卸载必须在删除插件目录前停止 Worker。
- 进程识别以持有的句柄为准，不按名称扫描和误杀进程。

## 10. 自动化调度

截图和 Feature 不并行争抢输入。每个调度周期：

```text
捕获一帧
→ 更新 GameContext
→ AutoDialogue 高优先级判断
→ AutoPick 低优先级判断
→ InputArbiter 提交至多一组动作
```

原则：

- 同一帧只产生一组最终输入。
- 自动剧情运行时可以抑制自动拾取。
- Feature 产生“动作意图”，由 Input Arbiter 统一执行。
- 紧急停止直接清空动作队列并阻止后续输入。
- 调度器、Feature 和测试均使用 `IClock`，避免硬编码睡眠影响回放测试。

## 11. BetterGI 导入与选择性同步策略

BetterGI 源码初始基线固定为 `0.62.1-alpha.2`、commit `0eb90304c4e4fa1f5cee2a4cbf68de6c8200ec94`。源码基线和运行资源基线分别记录：当前已验证的本机发行资源来自 BetterGI `0.62.0`，正式导入前必须用可重复下载的发行或 alpha 构件替代本机路径，并记录构件与逐文件 SHA-256。

同步遵循以下原则：

1. 日常监控 `origin/main`，但只从明确选定的 commit 或正式 Release 导入。
2. 源码、默认黑名单、剧情关键词、模板、OCR/VAD 模型作为同一个兼容性变更集审查。
3. Port 层尽量保持上游形状；Akasha 命名、依赖注入和动作意图放在 Adapter 层。
4. 每次同步先生成提交级差异报告，再由人工选择 `adopt`、`translate`、`defer` 或 `ignore`。
5. LLM 可以总结语义差异、生成候选补丁和测试，但不能绕过编译、回放、素材完整性与人工输入安全审查。
6. 同步提交不同时优化识别算法；行为改变必须有独立测试和来源记录。

初次导入步骤：

1. 复制 AutoPick、AutoSkip 及实际编译依赖到 `AkashaAutomation.BetterGiPort/Upstream`。
2. 建立永久兼容层，将 `TaskContext.Instance()`、`Global.Absolute()`、`App.GetLogger<T>()`、`VisionContext`、消息框与输入调用映射到 Core。
3. 删除 WPF UI、ViewModel、托盘、更新器和无关任务依赖。
4. 从固定发行构件提取默认黑名单、剧情关键词、模板和模型，提交小型运行配置，记录大型模型的来源与哈希。
5. 先建立无真实输入的帧回放基线，再允许 Worker 执行动作意图。

同步元数据至少包含：

```text
sourceCommit
sourcePaths
releaseVersion
releaseArtifactSha256
runtimeAssetPathsAndHashes
localPatches
lastReviewedUpstreamCommit
```

上游兼容调用映射为：

```text
TaskContext.Instance()  → BetterGiRuntimeContext → IGameContext
App.GetLogger<T>()      → BetterGiLogAdapter     → ILogger<T>
Global.Absolute()       → BetterGiAssetResolver  → IAssetPathResolver
Simulation/PostMessage  → BetterGiInputAdapter   → 动作意图 / IInputService
VisionContext           → BetterGiDiagnostics    → IDiagnosticsSink
ThemedMessageBox        → 日志与 Worker 状态事件
```

## 12. 测试策略

### Core 单元测试

- 窗口和截图生命周期。
- 坐标、缩放和 ROI 转换。
- OCR 资源释放。
- 调度取消和异常隔离。
- Input Arbiter 的动作互斥。

### BetterGI Port 测试

- 上游资源清单、哈希和必需文件完整性。
- 默认拾取黑名单与剧情关键词可解析。
- Compatibility 到 Core 接口的调用映射。
- Port 不引用 WPF UI、ViewModel、更新器或 Worker 生命周期代码。
- 选定上游行为与固定帧基线的一致性。

### Feature 测试

- 自动拾取黑白名单和 NPC/机关排除。
- 自动剧情选项、黑屏、书页、提交物品和邀约分支。
- OCR 失败回退。
- 禁用 Feature 后不产生动作。

### 帧回放测试

回放测试使用录制帧和 `RecordingInputService`，禁止真实输入：

```text
输入帧 + 配置 + 虚拟时间
→ 执行 Feature
→ 比较动作意图、OCR 结果和状态事件
```

至少覆盖 1080p、1440p 和不同 UI 缩放情形。

### Worker 集成测试

- 握手成功和协议不兼容。
- 非法令牌拒绝。
- 重复启动和单实例。
- 正常关闭、强制关闭和管道断开。
- 更新/卸载前文件句柄释放。
- 父进程退出后 Worker 自动结束。

## 13. 发布目录

开发仓库不提交 Worker 构建产物。CI 执行：

```text
dotnet test
→ dotnet publish AkashaAutomation.Worker
→ 复制 Worker 输出到插件包 runtime/
→ 生成文件哈希清单
→ 验证 plugin.json、入口脚本和 Worker
→ 打包 akasha-genshin-automation.zip
```

用户安装后的结构：

```text
User/Data/InstalledPlugins/akasha-genshin-automation/
├─ plugin.json
├─ main.js
├─ panel/
└─ runtime/
   ├─ AkashaAutomation.Worker.exe
   ├─ AkashaAutomation.Worker.dll
   ├─ OpenCvSharpExtern.dll
   ├─ OCR 与 ONNX 原生依赖
   └─ Assets/
```

清单中的哈希只能发现损坏或非预期修改；要防止恶意插件作者替换 manifest 和 Worker，仍需要可信插件源或签名机制。

## 14. 实施阶段

### Phase 0：来源与工程边界

- 新增 BetterGiPort 项目并固定依赖方向。
- 建立源码、发行构件、资源路径和 SHA-256 清单。
- 实现可重复的资源导入与验证脚本。

### Phase 1：Companion 纵向切片

- 在 AkashaNavigator 实现 manifest、权限和进程管理。
- 使用 Echo Worker 验证启动、调用、停止、更新和卸载。

### Phase 2：Worker 基础设施

- Generic Host composition root、Worker 状态机和稳定状态 DTO。
- 高优先级紧急停止、有界命令队列、结构化日志和安全资源释放顺序。
- 将命名管道断开、父进程退出和宿主取消统一接入急停优先的关闭流程。

### Phase 3：识别与回放基础设施

- 游戏窗口发现、截图、OCR、输入和调度接口。
- 截图帧、ROI、模板匹配、OCR、虚拟时间和 RecordingInputService。
- RecordingInputService 与动作意图断言。
- BetterGI 来源清单、发行资源提取和哈希验证。

### Phase 4：自动拾取纵向切片

- 迁移交互键、图标、OCR、默认黑名单、用户黑白名单和优先级规则。
- 建立回放基线并验证 NPC、机关和特殊硬编码排除。
- 从识别结果到 Input Arbiter 完成首条真实功能链路。

### Phase 5：自动剧情迁移

- 迁移对话推进、选项优先级、黑屏、弹窗、提交物品和邀约分支。
- 将 Silero VAD 语音等待作为独立子阶段接入，缺失模型时安全回退。
- 建立回放基线，去除 BetterGI UI 与进程生命周期依赖。

### Phase 6：插件体验

- 插件设置面板、状态、热键、错误反馈和紧急停止。

### Phase 7：同步、发行与验收

- 插件 ZIP 流水线。
- BetterGI 更新报告与选择性同步流水线。
- 许可证与第三方素材清单。
- 更新、卸载、异常退出和真实游戏验收。

## 15. 首版验收条件

- 用户只安装 Akasha 插件即可运行。
- 没有 BetterGI 安装目录和进程时功能正常。
- 插件启用后自动启动 Worker。
- 插件禁用、Profile 切换、更新和卸载后无残留进程。
- Akasha 崩溃或通信断开后 Worker 停止输入并退出。
- 自动拾取与自动剧情回放结果符合固定基线。
- 测试模式不会产生真实键鼠输入。
- Worker 日志可以定位截图、OCR、Feature 和输入阶段故障。

## 16. 已确定的设计决策

- 自动化源码位于 `AkashaPlugins/plugins/akasha-genshin-automation`。
- Worker 产品名：`AkashaAutomation.Worker`。
- BetterGI 作为可追溯上游，按固定版本窗口选择性同步相关代码与发行资源，不持续镜像 `main`。
- 剥离后使用 `AkashaAutomation.*` 命名空间。
- BetterGI Port 内允许保留上游命名空间和结构；对外 API 统一使用 `AkashaAutomation.*`。
- Akasha 与 Worker 使用命名管道，不监听固定 HTTP 端口。
- companion 不允许插件传入任意命令行。
- Worker 使用独立进程并由 Akasha 监管。
- 剥离阶段先保持行为，再进行结构重构。
