# hello —— 全功能巡览

把 `ctx.*` 的部件差不多都用了一遍，每处配了注释。想「查某个 API 怎么用」时翻这里。

> **需要先生成 `refs/`**：hello 无条件用到 Toast / GameAPI / Hook，所以不能纯 Core 构建。
> 想先看零配置就能编的例子，去 [`../roundnote`](../roundnote/README.md) 或 [`../../my-mod`](../../my-mod/README.md)。

| 想看什么 | 位置（`src/HelloMod.cs`） |
|---|---|
| `ctx.Data` 读写 | `OnLoad` 开头 |
| `ctx.Config.Bind`（bool / int / enum）与 `OnChanged` | `OnLoad` |
| `ctx.Input.RegisterHotkey` 并检查返回值 | `OnLoad` |
| `ctx.Services.Register / Get` | `OnLoad` |
| `ctx.Events.OnRoundStarted`、`OnNetIn<T>` | `OnLoad` |
| `ctx.MainThread.Delay / EveryFrame` 与 `ISubscription.Cancel` | `OnLoad`、`OnDisable` |
| `ctx.Hooks.Prefix / Postfix`：改参数、`Skip`、处理器异常隔离 | `InstallHooks` 及其后的处理器（由配置项 `hooks.*` 控制，默认不装） |
| 生命周期回调顺序 | `OnGameReady` / `OnSceneChanged` / `OnUpdate` / `OnDisable` |

## 编译

```bash
# 先生成 refs/（从你自己的游戏），否则会报错提示
tools/yx-patch refs --game "<你的弈仙牌安装目录>" --out refs

dotnet build examples/hello/Hello.csproj -c Release
```
