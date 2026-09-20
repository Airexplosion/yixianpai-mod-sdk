# 上手

## 前置

- **.NET 8 SDK**（或更高，`dotnet --version` 能跑就行）。仓库里的工具和测试都在 net8 上跑。
- **弈仙牌 MOD 管理器**（用来把 mod 装进游戏 / 实机测试）。
- 想写碰游戏的功能，还要有一份**你自己安装的弈仙牌**（Steam 版）——用来生成 `refs/`（见下）。

## 五分钟跑通

```bash
# 1. fork 本仓库，clone 你的 fork
git clone https://github.com/<你的用户名>/yixianpai-mod-sdk.git
cd yixianpai-mod-sdk

# 2. 直接编一个示例（不需要游戏、不需要任何配置）
dotnet build examples/roundnote/RoundNote.csproj -c Release

# 3. 跑它的测试
dotnet test examples/roundnote/tests/RoundNote.Tests.csproj
```

能编过、测试全绿，环境就没问题了。这一步是「纯 Core 模式」——只用到不碰游戏的 API（`ctx.Log` / `Config` / `Input` / `Events` …），所以零配置。

## 两种构建模式

同一个 mod 工程，按有没有 `refs/` 自动切换：

| | 需要什么 | 能用什么 | 碰游戏的代码（`#if YX_GAME`） |
|---|---|---|---|
| **纯 Core** | 只要仓库自带的 `sdk/` | `ctx.*` 全部 | 不编进去 |
| **游戏模式** | 再加 `refs/`（你自己生成） | 再加 Toast / GameAPI / Hook / 游戏类型 | 编进去 |

写碰游戏的代码时放进 `#if YX_GAME ... #endif`，这样没有 `refs/` 的人（包括 CI、只想读逻辑的人）也能编译和测试你的纯逻辑部分。示例见 `examples/roundnote/src/`。

## 生成 refs/（要写碰游戏的功能才需要）

`refs/` 是游戏 / Unity 的引用 DLL。**它是厂商版权，本仓库不含、也永远不要提交**（`.gitignore` 已经挡住）。你从**自己安装的游戏**里生成：

```bash
tools/yx-patch refs --game "<你的弈仙牌安装目录>" --out refs
```

（游戏目录就是含 `YiXianPai.exe` 的那个文件夹。）生成后，同一条 `dotnet build` 就会自动带上 `YX_GAME`，`#if YX_GAME` 里的代码开始编译。

> 游戏经常更新。更新后重新跑一次 `yx-patch refs` 刷新 `refs/`，再重编。

## 开始写你自己的 mod

1. 复制 `my-mod/` 改个名（目录名、`AssemblyName`、`RootNamespace`、`manifest.json` 里的 `id` / `name` / `entry` / `entryType`）。
2. 在 `src/` 里写代码，入口类继承 `YxMod`、重写 `OnLoad`。
3. `dotnet build 你的目录/你的.csproj -c Release` → 产物在 `你的目录/plugins/`。
4. 校验：`tools/yx-patch check 你的目录/plugins/你的.dll`（ILRuntime 约束，见 [ilruntime-rules.md](ilruntime-rules.md)，必须 0 error）。
5. 测试：`dotnet test 你的目录/tests/...`（见 [testing.md](testing.md)）。
6. 打包：`tools/yx-patch pack 你的目录` → 出 `dist/<id>-<version>.zip`。
7. 装进游戏：在弈仙牌 MOD 管理器里把仓库目录加为「开发目录」，或直接安装那个 zip；**建议开沙箱模式**。

## 接着读

- [two-layer-model.md](two-layer-model.md) —— Core / 游戏绑定层怎么分，为什么这么分。
- [sdk-reference.md](sdk-reference.md) —— `ctx.*` 都有什么。
- [hooks.md](hooks.md) —— 钩游戏方法。
- [manifest.md](manifest.md) —— `manifest.json` 每个字段。
- [ilruntime-rules.md](ilruntime-rules.md) —— **务必读**：ILRuntime 下的写法禁区（不然会在游戏里静默出错）。
- [testing.md](testing.md) —— 用 `TestHost` 在纯 .NET 下测你的 mod。
