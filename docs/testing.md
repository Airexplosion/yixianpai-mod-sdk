# 测试：`TestHost`

`Yx.ModSdk.Testing.TestHost`（netstandard2.0）在普通 .NET 里按与游戏内 Loader 相同的生命周期驱动 mod：登记 → 实例化 → `OnLoad` → 配置 / 事件 / 热键 / 时钟 / 数据 / 钩子 → 停用 / 热重载。不需要游戏、不需要 `refs/`。测试工程（模板 `tests/`）是 net8.0 + xunit，引用 `sdk-bin/netstandard2.0/` 下的 `Yx.ModSdk.Core.dll` 与 `Yx.ModSdk.Testing.dll`，并把 `../src/ModMain.cs` 直接 `Compile Include` 编进来（不是 `ProjectReference`：`src` 是 net40 的游戏内程序集）。因此入口类只能用 `Yx.ModSdk.Core` 的 API；调 `Ui.Toast` / GameAPI 的代码放到单独文件里，测试工程不编入它。

测试代码本身跑在 CLR 上，可以随便用 LINQ；**被测的 mod 代码仍要遵守 [ilruntime-rules.md](ilruntime-rules.md)**。

## 必须关并行

`TestHost` 是进程级的（背后是 `SdkRuntime` 的静态状态），同一进程内只能串行使用。xunit 工程里加一个文件：

```csharp
// AssemblyInfo.cs
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
```

没加的话并行用例会互相踩运行时，`TestHost.Start()` 抛 `InvalidOperationException("上一个 TestHost 尚未 Dispose")`。

## 基本形状

```csharp
using System.Linq;
using Xunit;
using Yx.ModSdk;
using Yx.ModSdk.Testing;
using Yx.Shared;

public class ModMainTests
{
 const string Id = "com.example.demo";

 [Fact]
 public void LoadsAndGreetsOnHotkey()
 {
 using (TestHost host = TestHost.Start(gameVersion: "9.9"))
 {
 ModMain mod = host.Load<ModMain>(Id, "1.0.0");
 Assert.NotNull(mod);
 Assert.Equal(ModStates.Running, host.Find(Id).State);
 Assert.Equal(1, host.PressHotkey("F8"));
 Assert.Contains("greet", host.Logs(Id).Select(l => l.Msg));
 }
 }
}
```

`using` 保证 `Dispose`；`Dispose` 后 `Logs`/`Events` 仍可读，其余方法抛 `ObjectDisposedException`。

## 全部入口

### `TestHost.Start(string gameVersion = "")`

初始化运行时，`gameVersion` 进 `ctx.Versions.Game`。上一个未 `Dispose` 时抛异常。

### `T Load<T>(id, version, config = null, data = null)`

登记并加载一个 mod（`T : YxMod, new()`）。返回 mod 实例；**`OnLoad` 抛异常或实例化失败时返回 null**，此时看 `Find(id).State`（`failed`）与 `LastError`：

```csharp
BrokenMod mod = host.Load<BrokenMod>(Id, "1.0.0");
Assert.Null(mod);
ModRuntime rt = host.Find(Id);
Assert.Equal(ModStates.Failed, rt.State);
Assert.Contains("boom", rt.LastError);
Assert.Contains(host.Logs(Id), l => l.Lvl == "error" && l.Msg.Contains("boom"));
```

`config` 是 section → key → value 的嵌套字典，`data` 是 key → value；模拟管理器从 JSON 下发，所以数值用 `double`（`7.0`），`ConfigEntry<int>` / `Data.Get<int>` 会转换：

```csharp
var config = new Dictionary<string, object> { ["general"] = new Dictionary<string, object> { ["count"] = 7.0 } };
ModMain mod = host.Load<ModMain>(Id, "1.0.0", config: config);
Assert.Equal(7, mod.Count.Value);
```

### `T Reload<T>(id, version, config = null, data = null)`

热重载：停用旧实例（若在运行）并用新的 config / data 重新加载，`Generation` 加一；失败返回 null。不带 `data` 即清空数据（与游戏内一致）。

```csharp
ModMain again = host.Reload<ModMain>(Id, "1.0.1");
Assert.Equal(2, host.Find(Id).Generation);
```

### `void SetConfig(id, nested)`

模拟管理器下发配置；只有值变化的键触发 `OnChanged` 与 `configChanged` 事件：

```csharp
host.SetConfig(Id, new Dictionary<string, object> { ["general"] = new Dictionary<string, object> { ["count"] = 9.0 } });
Assert.Equal(9, mod.Count.Value);
Assert.Contains(host.Events("configChanged"), e => (string)e["key"] == "count");
```

### `void Tick(double seconds = 0.016)`

推进时钟并跑一帧：任务队列（`Post`、到期的 `Delay`、`EveryFrame`）→ `OnUpdate`，顺序与游戏内 `Pump` 一致。mod 回调的异常照常由宿主记账；基础设施自身的异常直接抛给测试。

```csharp
host.Tick(0.5);
Assert.False(mod.DelayFired); // Delay(1.0f, ...) 还没到
host.Tick(1.0);
Assert.True(mod.DelayFired);
```

### `int PressHotkey(string binding)`

按绑定字符串触发热键（大小写不敏感，`"f8"`、`"ctrl+shift+d"` 都行）。**返回被触发的回调个数**，未登记的组合返回 0——用它断言热键确实注册上了。

### `void Publish(string topic, object payload)`

往事件总线发一条：`host.Publish(Topics.Round, 4)`（`Topics` 在 `Yx.ModSdk.Hosting`）。`OnNetIn<T>` 用 `Topics.NetIn` 配合一个 `T` 实例。

### `void SetGameReady()` / `void SetScene(GameScene scene)`

触发 `OnGameReady`（只一次）与 `OnSceneChanged`（同场景不重复）。

### `bool SoftDisable(string id)`

模拟管理器停用：`OnDisable`、自动清理、状态 `disabled`。不在运行返回 false。

```csharp
Assert.True(host.SoftDisable(Id));
Assert.Empty(host.Hooks.Sites.Single().Snapshot()); // 钩子处理器已清
```

### `ModRuntime Find(string id)`

`State`、`LastError`、`Generation`、`Version`；未登记返回 null。

### `LogItem[] Logs(string src = null)`

到目前为止的日志（`Seq`、`Src`、`Lvl`（`"debug"/"info"/"warn"/"error"`）、`Msg`）；`src` 为 mod id 时只取该 mod 的。

### `Dictionary<string, object>[] Events(string kind = null)`

非日志事件：`configChanged`、`dataChanged`、`modState`、`configSchema`、`scene`、`gameReady`、`hotkeyConflict`。字段与管理器收到的一致（如 `modState` 有 `id`/`state`/`version`/`generation`/`reason`/`error`）。

### `int Dropped`

`LogBuffer` 因容量满（5000）丢弃的条数累计，正常测试应为 0。

### `double Now`

当前虚拟时钟（秒）。

### `HookRegistry Hooks`

站点表：`host.Hooks.Sites` 拿 `HookSite[]`（`Key`、`Applied`、`HandlerCount`、`Snapshot()`）。`TestHost` 的安装器只把 `Applied` 置 true，不做 IL 重写。

### `void FailHookInstall(string typeFullName, string method, string reason)`

让之后对这个方法登记钩子时失败（模拟游戏更新后方法改名 / 无法改写）：`ctx.Hooks.Prefix / Postfix` 抛 `InvalidOperationException(reason)`，`TryPrefix / TryPostfix` 与钩子组则记 warn、返回 null。用来测「钩子不全时功能要关掉」这类降级逻辑。要在 `Load` **之前**调（钩子一般在 `OnLoad` 里登记）；随 `Dispose` 清空。

### `bool InvokePrefix(type, method, paramCount, self, args, out object result)`

模拟被钩方法被调用时的前置分发：返回 true 表示原方法应被跳过，`result` 为替代返回值。**站点必须已由 mod 登记**，否则抛 `InvalidOperationException("没有登记的钩子站点：Type.Method/N")`。

### `object InvokePostfix(type, method, paramCount, self, args, object result)`

模拟原方法返回后的后置分发，返回（可能被改写的）返回值。同样要求站点已登记。

```csharp
object result;
Assert.False(host.InvokePrefix("CardPanel", "GetHandCards", 0, null, new object[0], out result));
mod.SkipHooked = true;
Assert.True(host.InvokePrefix("CardPanel", "GetHandCards", 0, null, new object[0], out result));
Assert.Equal("skipped", result);
Assert.Equal("r", host.InvokePostfix("CardPanel", "GetHandCards", 0, null, new object[0], "r"));
```

## 常见断言

| 想验证 | 写法 |
|---|---|
| 加载成功 | `Assert.NotNull(mod)`；`host.Find(Id).State == ModStates.Running` |
| 某条日志出现 | `Assert.Contains("loaded", host.Logs(Id).Select(l => l.Msg))` |
| 没有 error 日志 | `Assert.DoesNotContain(host.Logs(), l => l.Lvl == "error")` |
| 配置 schema 上报了某项 | `host.Events("configSchema").Single()["entries"]` 里找 `section`/`key` |
| 数据写回 | `host.Events("dataChanged").Single()["value"]` |
| 热键冲突 | 两个 mod 绑同一键后 `host.Events("hotkeyConflict")` 非空 |
| 停用后无残留 | `SoftDisable` 后 `PressHotkey` 返回 0、`Hooks.Sites[i].Snapshot()` 为空 |

## 完整示例

`sdk/Yx.ModSdk.Testing.Tests/`：`SampleMod.cs`（覆盖 `ModContext` 每个能力、按热更约束书写）、`BrokenMod.cs`、`TestHostTests.cs`（13 个用例）。跑：`dotnet test sdk/Yx.ModSdk.Testing.Tests`。模板生成的 `tests/ModMainTests.cs` 是精简版：加载并断言版本日志、`PressHotkey("F9")` 按配置次数打招呼、`Publish(Topics.Round, 3)` 写 debug 日志。
