# Akasha 原神自动化

AkashaNavigator 的进程外原神自动化插件，前端脚本与
`AkashaAutomation.Worker` 作为一个版本配套发布。

当前提供：

- 自动拾取，默认快捷键 `F9`；
- 自动剧情，默认快捷键 `F12`；
- 前台游戏窗口约束、输入仲裁和紧急停止；
- 固定 companion protocol v1；
- BetterGI 来源、模型和资源的哈希化派生记录。

两个功能默认关闭。真实输入仅在原神窗口位于前台时发送。如果原神以管理员
权限运行，AkashaNavigator 也必须以管理员权限运行，否则 Windows 会拦截输入。

## 安装与更新

插件中心从 AkashaPlugins `catalog` 读取版本，并从同仓库 GitHub/CNB Release
下载 `akasha-genshin-automation-<version>-win-x64.zip`。安装前会显示
companion 可执行文件和权限；更新时前端与 Worker 原子替换，Profile 配置和
`data/` 用户数据会保留。

也可以把 Release ZIP 作为离线包导入，不要手工拆分或替换其中的 Worker。

## 开发

后端不引用 AkashaNavigator，可独立构建和测试：

```powershell
dotnet test ./backend/AkashaAutomation.sln -c Release
```

生成并二次校验 Release 包：

```powershell
./packaging/Publish-Plugin.ps1
```

与相邻 AkashaNavigator 源码运行 companion 协议烟雾测试：

```powershell
./backend/scripts/Test-NavigatorCompanion.ps1
```

架构与本地实机测试说明位于 `backend/docs/`。BetterGI 派生和第三方许可分别
记录在 `DERIVATION.md`、`THIRD_PARTY_NOTICES.md` 与 `backend/upstream/`。
