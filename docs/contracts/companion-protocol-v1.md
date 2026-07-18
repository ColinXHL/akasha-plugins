# Companion Protocol v1

## 状态与范围

本文件冻结 AkashaNavigator 与插件伴随进程之间的通用 IPC 协议版本 `1`。
协议不能包含原神、自动拾取或自动剧情等插件专属判断。

现有 AkashaNavigator 与 Akasha Automation Worker 已实现此协议的基础传输和握手；
本文件将其固化为跨仓库契约。

## 进程启动

宿主以当前完整性级别启动 Worker，不使用 Shell，也不请求提权。工作目录是
`backend.entry` 所在目录。宿主传入且 Worker 必须严格解析以下参数：

```text
--pipe <name>
--token <session-token>
--parent-pid <positive-integer>
--protocol-version 1
```

约束：

- 管道名由宿主随机生成，只包含 ASCII 字母、数字、`.`、`_`、`-`；
- 会话令牌由加密安全随机数生成，每次启动不同；
- Worker 拒绝未知、重复或缺失参数；
- Worker 必须监控父进程，父进程退出后自行退出；
- Windows 管道限制为当前用户；
- 宿主使用 Job Object 确保异常退出时清理子进程。

## 帧格式

每条消息由以下两部分组成：

1. 4 字节 little-endian 有符号整数，表示 JSON UTF-8 字节数；
2. 对应长度的 UTF-8 JSON。

有效负载长度必须在 `1..262144` 字节之间。JSON 属性使用 camelCase，属性名区分
大小写，发送端省略值为 `null` 的可选属性。

## Envelope

```json
{
  "type": "request",
  "correlationId": "32 位十六进制请求 ID",
  "method": "worker.status",
  "payload": {}
}
```

公共字段：

| 字段 | 用途 |
| --- | --- |
| `type` | `hello`、`welcome`、`request`、`response`、`event` 或 `shutdown` |
| `correlationId` | 关联 request 与 response |
| `method` | RPC 方法名 |
| `payload` | 方法相关 JSON |
| `protocolVersion` | 握手协议版本 |
| `token` | 仅用于 `hello` |
| `workerVersion` | Worker 构建版本 |
| `parentProcessId` | Worker 确认的宿主 PID |
| `accepted` | `welcome` 是否接受连接 |
| `error` | `{ "code": "...", "message": "..." }` |

## 握手

Worker 连接后首先发送：

```json
{
  "type": "hello",
  "protocolVersion": 1,
  "token": "<session-token>",
  "workerVersion": "<version>",
  "parentProcessId": 1234
}
```

宿主使用固定时间比较验证令牌，并检查协议版本和父进程 PID。接受时返回：

```json
{
  "type": "welcome",
  "protocolVersion": 1,
  "accepted": true
}
```

拒绝时返回 `accepted=false` 和机器可读错误，然后关闭管道并终止该 Worker。

## 请求和响应

- `request` 必须包含非空 `correlationId` 与 `method`；
- `response` 必须复用请求的 `correlationId`；
- 成功响应可以包含 `payload`；
- 失败响应必须包含 `error.code` 和 `error.message`；
- 宿主默认单次调用超时 10 秒；
- 每个 session 最多同时存在 64 个待处理请求；
- 宿主为 `automation.emergencyStop` 和 `worker.shutdown` 保留一个请求容量。

方法名命名空间：

- `worker.*`: 通用生命周期与状态；
- `automation.*`、`features.*`: 自动化插件私有能力；
- 其他插件使用自己的稳定命名空间。

`event` 在 v1 中是保留消息类型。AkashaNavigator 在实现事件分发和背压规则前不得
要求 Worker 发送 `event`；当前正式通信只使用握手、请求、响应和关闭消息。

## 关闭

宿主先调用 `worker.shutdown` 请求正常关闭。Worker 返回接受响应后停止功能、释放输入
和捕获资源并退出。

Worker 还必须接受 `type=shutdown` 控制帧，供无法执行 RPC 的兼容宿主使用。
超过 `backend.shutdownTimeoutMs` 后，宿主才允许通过 Job Object 受控终止进程树。

插件停用、卸载、更新、宿主退出和紧急停止均不能依赖 JavaScript 插件仍可正常
执行。

## 兼容性

`protocolVersion` 只参与握手，不参与用户可见的插件版本比较。协议不匹配时拒绝
启动，不尝试降级猜测。新增可选 envelope 字段允许保持 v1；改变帧格式、握手必需
字段或既有字段语义必须提升协议版本。
