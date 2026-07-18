# Plugin Manifest v2

## 状态

本文件冻结 Akasha 官方仓库的插件清单协议版本 `2`。机器可读定义位于
[`schemas/plugin-manifest.schema.json`](../../schemas/plugin-manifest.schema.json)。

插件源码目录和 `catalog` 目录都使用文件名 `manifest.json`。旧版
`plugin.json` 只作为迁移输入，不作为新仓库的公共契约。

## 必需字段

| 字段 | 说明 |
| --- | --- |
| `manifestVersion` | 固定为 `2` |
| `id` | 稳定、全局唯一的小写插件 ID |
| `name` | 用户可见名称 |
| `version` | SemVer 2.0.0 版本 |
| `description` | 用户可见说明 |
| `authors` | 至少一个作者 |
| `host.minVersion` | 最低 AkashaNavigator 版本 |
| `main` | 插件目录内的 JavaScript 入口 |
| `permissions` | 权限声明，必须是宿主已知权限 |
| `savedFiles` | 更新时跨版本保存的相对路径 |
| `distribution` | `repository` 或 `release` |

所有文件路径使用 `/`，必须是插件目录内的安全相对路径，不允许盘符、绝对路径、
反斜杠或 `..` 路径段。

## 可选字段

- `settings`: 设置界面描述文件。
- `homepage`: 插件主页。
- `profiles`: 推荐使用该插件的 Profile ID，不代表自动授权或自动安装。
- `tags`: 插件中心的搜索标签。
- `library`: JavaScript 模块搜索目录。
- `httpAllowedUrls`: `network` 权限可访问的 URL 模式。
- `defaultConfig`: 默认配置。
- `backend`: 固定的伴随进程声明。

## 分发类型

### Repository

```json
{
  "distribution": {
    "type": "repository"
  }
}
```

安装器从 `catalog` 中的插件目录复制允许发布的文件。

### Release

```json
{
  "distribution": {
    "type": "release",
    "tag": "akasha-genshin-automation-v0.4.3",
    "asset": "akasha-genshin-automation-0.4.3-win-x64.zip",
    "sha256": "64 位小写十六进制 SHA-256",
    "size": 123456
  }
}
```

`main` 中的源清单允许暂时缺少 `sha256` 和 `size`，因为两者由 CI
构建后写入。发布到 `catalog` 前，两者必须存在并与 Release 资源回读结果一致。

标签和资源名称固定为：

```text
<plugin-id>-v<plugin-version>
<plugin-id>-<plugin-version>-win-x64.zip
```

## 后端声明

带后端的插件必须：

- 使用 `distribution.type=release`；
- 声明 `companion` 权限；
- 使用 `backend.type=companion-process`；
- 使用 companion protocol v1；
- 由宿主启动、停止并监控后端。

```json
{
  "backend": {
    "type": "companion-process",
    "entry": "runtime/AkashaAutomation.Worker.exe",
    "protocolVersion": 1,
    "lifetime": "plugin",
    "integrityLevel": "inherit",
    "shutdownTimeoutMs": 5000
  }
}
```

`singleInstance` 不再是 Manifest v2 字段。每个插件在每个宿主进程内只有一个
companion session，这是宿主协议约束。

## 旧清单迁移

| 旧 `plugin.json` | Manifest v2 |
| --- | --- |
| `author` | `authors[0].name` |
| `minAppVersion` | `host.minVersion` |
| `settings_ui.json` 约定 | 显式 `settings` |
| `http_allowed_urls` | `httpAllowedUrls` |
| `companion.executable` | `backend.entry` |
| `companion.protocolVersion` | `backend.protocolVersion` |
| `companion.lifetime` | `backend.lifetime` |
| `companion.singleInstance` | 删除，由宿主保证 |

旧字段不会长期同时写入新清单。兼容逻辑只存在于 AkashaNavigator 的数据迁移层。
