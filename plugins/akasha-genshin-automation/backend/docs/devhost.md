# 独立实机测试

本项目保留两个职责明确的独立测试程序，都不依赖 AkashaNavigator：

- `AkashaAutomation.DevHost`：只观察识别和决策，永远不发送键鼠输入。
- `AkashaAutomation.LiveTestHost`：人工验收真实输入，必须以管理员身份运行。

生产 Worker 仍不注册真实输入服务。

## 1. 先做只观察测试

自动拾取：

```powershell
dotnet run `
  --project .\src\AkashaAutomation.DevHost\AkashaAutomation.DevHost.csproj `
  --configuration Release `
  -- --feature auto-pick
```

自动剧情：

```powershell
dotnet run `
  --project .\src\AkashaAutomation.DevHost\AkashaAutomation.DevHost.csproj `
  --configuration Release `
  -- --feature auto-dialogue --option-strategy first
```

日志中的 `wouldPress=true` 或 `wouldAct=true` 仅表示规则准备执行；DevHost 始终发送 0 组输入。按 `Ctrl+C` 停止。

## 2. 再做真实输入测试

先发布唯一的真实输入测试目录：

```powershell
dotnet publish `
  .\src\AkashaAutomation.LiveTestHost\AkashaAutomation.LiveTestHost.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  --output .\artifacts\live-test-host
```

在管理员 PowerShell 中直接运行，不需要任何参数：

```powershell
.\artifacts\live-test-host\AkashaAutomation.LiveTestHost.exe
```

菜单只有两个开关，默认都开启：

1. 自动拾取。
2. 自动剧情。

按 `1`/`2` 切换开关，直接回车开始。自动剧情固定使用 BetterGI 默认的“优先选择第一个选项”，不再询问 VAD、自定义文字或其他参数。倒计时 3 秒后持续运行；切回控制台按 `Ctrl+C` 停止，游戏仍在前台时可按 `Ctrl+Alt+F12` 全局急停。切出游戏会自动拒绝输入。

测试循环使用 BetterGI 默认的 50 ms 触发周期，并扣除当前帧已经消耗的处理时间。自动拾取的裁剪文字区域直接走单行识别，不再额外运行文字检测模型，与 BetterGI 的 `OcrWithoutDetector` 路径一致。

自动剧情日志中的 `target=(x,y)@宽x高` 是本次识别得到的截图坐标。输入层会先按游戏当前客户区缩放，再转换为虚拟桌面坐标，因此支持窗口尺寸变化和多显示器位置。

开发入口和正式 Worker 默认使用 BitBlt 截取游戏客户区，不会触发 Windows Graphics Capture 的黄色系统边框。

## 3. 建议顺序

1. 用 DevHost 确认普通拾取物会产生 `pick`，烹饪/NPC/机关不会产生动作。
2. 用 LiveTestHost 验证持续自动拾取。
3. 用 DevHost 确认剧情分类、OCR 文本和选项策略。
4. 用 LiveTestHost 在无重要后果的 NPC 对话中验证空格推进和选项点击位置。

原神以管理员身份运行时，LiveTestHost 也必须是管理员权限；否则 Windows 可能报告输入已提交，但游戏不会响应。
