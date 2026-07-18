# Akasha Plugins

AkashaNavigator 的官方插件聚合仓库。

## 官方镜像

- GitHub: <https://github.com/ColinXHL/akasha-plugins>
- CNB: <https://cnb.cool/AkashaNavigator/akasha-plugins>

两个地址代表同一个逻辑插件仓库。GitHub 是源码与发布流程的权威仓库，CNB
提供相同 `catalog` 内容和发布产物的镜像。

## 分支模型

- `main`: 插件源码、后端源码、测试和构建脚本。
- `catalog`: 只包含客户端可消费的插件目录和 CI 生成的 `repo.json`。

`repo.json` 不允许手工维护。只有通过清单校验、测试、构建、Release
发布和产物回读验证的插件版本才能进入 `catalog`。

## 当前插件

- `bilibili-page-list`
- `genshin-direction-marker`
- `smart-cursor-detection`

每个插件目录均以 Manifest v2 声明稳定 ID、权限、入口、默认配置和更新时保留文件。

## 公共契约

仓库冻结并执行以下公共契约：

- [Manifest v2](docs/contracts/manifest-v2.md)
- [`repo.json` schema v1](docs/contracts/repository-index-v1.md)
- [Companion protocol v1](docs/contracts/companion-protocol-v1.md)
- [现有插件与数据迁移盘点](docs/migration/phase-0-inventory.md)

运行契约检查：

```powershell
python -m pip install -r requirements-dev.txt
python tests/validate_contracts.py
```
