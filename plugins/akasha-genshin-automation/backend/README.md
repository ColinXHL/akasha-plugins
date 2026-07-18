# Akasha Automation Backend

这是 `akasha-genshin-automation` 的独立进程后端。它负责窗口采集、识别、
PaddleOCR、调度和受约束输入；前端设置、快捷键和状态展示位于相邻
`frontend/`。

后端项目不引用 AkashaNavigator。生产 Worker 只通过 companion protocol v1
与宿主通信。默认识别资源随插件 Release 分发，用户配置通过协议传入。

## 构建与测试

```powershell
dotnet build ./AkashaAutomation.sln -c Release
dotnet test ./AkashaAutomation.sln -c Release
```

观察模式 DevHost 不包含真实输入：

```powershell
dotnet run --project ./src/AkashaAutomation.DevHost/AkashaAutomation.DevHost.csproj -c Release -- --pick-key F
```

管理员实机验收使用独立的 `AkashaAutomation.LiveTestHost`，操作前请阅读
[docs/devhost.md](docs/devhost.md)。

## BetterGI 资源

固定版本的 BetterGI 配置、模板与模型随源码保存，普通构建不会读取本机
BetterGI，也不会下载外部 Release。显式更新资产时运行：

```powershell
./scripts/Import-BetterGiAssets.ps1 -Source "C:\Program Files\BetterGI"
./scripts/Import-BetterGiAssets.ps1 -Source "./BetterGI_v0.62.0.7z" -VerifyOnly
```

机器可读来源和哈希位于 `upstream/bettergi/`，法律与派生说明位于插件根目录。

## 发布

发布入口在插件根目录的 `packaging/Publish-Plugin.ps1`。AkashaPlugins
Workflow 会先运行全部测试，再发布 Worker、组装前端和资源、验证 ZIP，
创建 GitHub/CNB Release，回读校验后才更新 `catalog`。
