# Repository Index v1

## 状态

本文件冻结 `catalog` 根目录 `repo.json` 的协议版本 `1`。机器可读定义位于
[`schemas/repository-index.schema.json`](../../schemas/repository-index.schema.json)。

`repo.json` 是插件中心快速索引，不替代每个插件的 `manifest.json`，也不允许手工
编辑。

## 结构

```json
{
  "schemaVersion": 1,
      "commit": "生成 catalog 的 main 源提交 SHA",
  "plugins": [
    {
      "id": "genshin-direction-marker",
      "path": "plugins/genshin-direction-marker",
      "name": "原神方向标记",
      "version": "1.1.0",
      "description": "识别攻略视频中的方位词，在小地图上显示方向标记",
      "distributionType": "repository",
      "hasBackend": false,
      "minHostVersion": "1.4.0"
    }
  ]
}
```

## 生成约束

生成器必须：

1. 只遍历 `plugins/*/manifest.json`；
2. 校验 Manifest v2；
3. 保证插件 ID 唯一；
4. 要求 `path` 精确等于 `plugins/<id>`；
5. 按插件 ID 使用 ordinal 顺序稳定排序；
6. 校验入口、设置和后端文件路径位于插件目录内；
7. 将 `distribution.type` 投影为 `distributionType`；
8. 将是否存在 `backend` 投影为 `hasBackend`；
9. 将 `host.minVersion` 投影为 `minHostVersion`；
10. 任一插件无效时停止生成，不发布部分索引。

`commit` 写入生成该 catalog 的 `main` 源提交 SHA。它不能写入包含
`repo.json` 的最终 catalog 提交 SHA，因为提交内容包含自身 SHA 会形成无法求解的
自引用。客户端从本地 Git 仓库读取 catalog HEAD，作为缓存版本和订阅记录中的
`installedCommit`。

生成过程必须可重复；索引不包含生成时间或其他会造成无意义差异的字段。
