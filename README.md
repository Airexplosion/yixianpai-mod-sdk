# 弈仙牌 Mod 开发 SDK

给弈仙牌（Steam 版）写 mod 的起步仓库。**Fork 它，照着走,你就能开发自己的 mod。**

```bash
git clone https://github.com/<你的用户名>/yixianpai-mod-sdk.git
cd yixianpai-mod-sdk
dotnet build examples/roundnote/RoundNote.csproj -c Release   # 零配置，先跑通
dotnet test  examples/roundnote/tests/RoundNote.Tests.csproj
```

编过、测试绿，就可以复制 `my-mod/` 开始写自己的了。完整上手见 **[docs/getting-started.md](docs/getting-started.md)**。

## 里面有什么

| 目录 | 是什么 |
|---|---|
| `my-mod/` | **起步模板**——复制改名就能写。纯 Core，零配置即编，带测试。 |
| `examples/roundnote/` | 教学示例：演示「纯 Core / `#if YX_GAME` 分层」，核心逻辑零配置即编、有测试；有 `refs/` 再点亮面板。 |
| `examples/hello/` | 全功能巡览：Toast、GameAPI、Hook、配置、热键、RPC 一网打尽。**需要先生成 `refs/`**。 |
| `sdk/` | SDK DLL（`Yx.ModSdk.Core` / `Yx.ModSdk` / `Yx.ModSdk.Testing`），已随仓库带好。 |
| `tools/` | `yx-patch`——生成 refs、代码校验、打包。 |
| `docs/` | 开发文档（上手、两层模型、API、Hook、manifest、ILRuntime 铁律、测试）。 |

## 前置

- **.NET 8 SDK**（`dotnet` 命令能用即可）。
- 想写碰游戏的功能：一份你自己安装的弈仙牌，用来生成 `refs/`（见下）。
- 实机测试：弈仙牌 MOD 管理器。

## 关于 refs/（重要）

碰游戏的代码要引用游戏 / Unity 的 DLL。**这些是厂商版权，本仓库不包含、你也永远不要提交**（`.gitignore` 已挡住 `refs/`）。你从**自己安装的游戏**里生成：

```bash
tools/yx-patch refs --game "<你的弈仙牌安装目录>" --out refs
```

没有 `refs/` 时，一切照常按「纯 Core」构建——不碰游戏的逻辑、测试都能跑，只是 `#if YX_GAME` 里的代码不编进去。有了 `refs/`，同一条 build 命令自动带上游戏能力。原理见 [docs/two-layer-model.md](docs/two-layer-model.md)。

## 许可

本仓库自带的内容（SDK、示例、模板、文档、工具）按 [MIT](LICENSE) 授权。
游戏 / Unity / 你生成的 `refs/` 不在此列，属各自厂商版权。
