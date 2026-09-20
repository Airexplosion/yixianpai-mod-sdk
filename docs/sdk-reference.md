# SDK 参考

命名空间：`Yx.ModSdk`（`YxMod`、`ModContext`、`GameScene`）、`Yx.ModSdk.Hooks`（`ModHooks`）、`Yx.ModSdk.Hosting`（`Topics`、`SdkRuntime`）、`Yx.ModSdk.Game`（GameAPI）、`Yx.ModSdk.Unity`（`Ui`、`SceneDetector`）、`Yx.Shared`（`ISubscription`、`HookContext`、`ModStates`）。示例都遵守 [ilruntime-rules.md](ilruntime-rules.md)。

## 入口与生命周期

```csharp
public sealed class ModMain : YxMod
{
 public override void OnLoad(ModContext ctx) { }
 public override void OnGameReady() { }
 public override void OnSceneChanged(GameScene scene) { }
 public override void OnUpdate() { }
 public override void OnDisable() { }
}
```

入口类由 `manifest.json` 的 `entryType`（完整类名）指定，必须公共、有公共无参构造函数。所有回调在 Unity 主线程执行。

| 回调 | 何时 | 说明 |
|---|---|---|
| `OnLoad(ctx)` | 登记后立即调用一次 | 绑定配置、注册热键、订阅事件、注册服务、登记钩子。**抛异常视为加载失败**，状态 `failed`；宿主会补调一次 `OnDisable`（让你清理已经建了一半的东西——所以 `OnDisable` 里用到的字段要判空），此后不再有任何回调。`OnLoad` 返回后宿主上报配置 schema（`configSchema` 事件） |
| `OnGameReady()` | 首次进入大厅 / 对局（`SceneDetector` 识别到 `Lobby`/`Placement`/`Battle`）调用一次 | 热重载后若游戏已就绪立即补调 |
| `OnSceneChanged(scene)` | 场景变化（每 0.5 s 轮询一次） | `GameScene`：`Unknown=0, Lobby=1, Placement=2, Battle=3, Home=4`。`Unknown`（加载过渡）不触发。加载后若已有场景立即补调。已知：结算画面识别为 `Placement`，加载 / 选角识别为 `Lobby` |
| `OnUpdate()` | 每帧 | 不需要就不要覆写 |
| `OnDisable()` | 软停用、错误熔断、热重载前 | 销毁自己创建的界面对象。**热键、事件订阅、主线程任务、服务、钩子由宿主自动清理**，不必手动 `Cancel`。异常只记 warn，不影响停用 |

主循环每帧顺序（`Bootstrap.Pump`）：任务队列（`Post`/`Delay`/`EveryFrame`）→ 热键 → 场景轮询（每 0.5 s）→ `OnUpdate` → Loader 面板。

### `ctx` 与 `Context`

`OnLoad(ModContext ctx)` 的参数和基类属性 `Context` 是同一个对象。约定：`OnLoad` 里用参数 `ctx`，其余回调与私有方法用 `Context`。

### 状态与错误预算

`ModStates`：`pending` → `running` → `disabled`（管理器 / 测试停用）或 `failed`（加载失败或熔断）。

每次回调（生命周期、热键、事件、配置回调、主线程任务、钩子处理器）都被 try/catch 包住：异常记 error 日志并计入该 mod 的 `ErrorBudget`。**60 秒滑动窗口内第 5 次异常**触发熔断：状态 `failed`、`OnDisable`、自动清理，日志"短时间内连续出错，已自动停用"。热重载会重置预算。

`ctx.Log.Error(msg, ex)` 只写日志，不计入预算；主动抛出才计入。

## `ctx.ModId` / `ctx.Version`

manifest 的 `id` 与 `version`。`id` 不能以 `yx.` 开头（宿主保留）。

## `ctx.Log`

`Debug/Info/Warn/Error(string)`、`Error(string, Exception)`。进入 `LogBuffer`（容量 5000 条，管理器定期 Drain；缓冲满时**新**日志被丢弃并计数，Drain 后恢复）。

```csharp
ctx.Log.Info("loaded v" + ctx.Version);
```

## `ctx.Config`

`ConfigEntry<T> Bind<T>(section, key, defaultValue, description)`，T 限 `bool`/`int`/`float`/`string`/enum；其它类型抛 `NotSupportedException`。`ConfigEntry<T>`：`Value`（读写）、`Section`、`Key`、`OnChanged(Action<T>)`。值由管理器读写 `config/<modId>.json` 并在运行中下发，游戏内不做文件读写。只有值真正变化的键会触发 `OnChanged` 与 `configChanged` 事件。enum 以名字存储，大小写不敏感解析，解析失败回默认值。

```csharp
_count = ctx.Config.Bind("general", "count", 3, "问候次数");
_count.OnChanged(OnCountChanged);

void OnCountChanged(int value)
{
 Context.Log.Info("count " + value.ToString(CultureInfo.InvariantCulture));
}
```

## `ctx.Input`

`bool RegisterHotkey(id, defaultBinding, Action callback)`：绑定写法 `"F8"`、`"Ctrl+Shift+D"`（大小写不敏感，修饰键顺序 CTRL、SHIFT、ALT）。可用键：字母、数字、F1–F15、`TAB`/`SPACE`/`ENTER`/`ESC`/方向键/`BACKQUOTE`/`MOUSE0-2`。热键自动成为配置项 `hotkeys.<id>`（类型 `hotkey`），用户在管理器改键后即时生效。组合键已被占用时返回 `false`、记 warn、发 `hotkeyConflict` 事件，该热键暂不生效直到改成未占用的组合。`string GetBinding(id)` 读当前绑定。

```csharp
bool ok = ctx.Input.RegisterHotkey("greet", "F8", Greet);
```

## `ctx.Events`

全部返回 `ISubscription`（`Cancel()` / `IsActive`），停用时自动取消。

| 方法 | 触发 |
|---|---|
| `OnRoundStarted(Action<int>)` | 摆牌 / 战斗场景下回合号变化 |
| `OnBattleStarted(Action)` | `Placement` → `Battle` |
| `OnBattleEnded(Action)` | 离开 `Battle` |
| `OnHandChanged(Action<IList<int>>)` | 摆牌阶段手牌 id 列表变化；每个订阅者拿到独立拷贝 |
| `OnNetIn<T>(Action<T>)` / `OnNetOut<T>(Action<T>)` | 服务器发来 / 客户端发出的协议消息，`T` 为 `Proto.*` 类型（如 `Proto.GameStatus`）；只有类型匹配的消息回调（`T` 为 `object` 收全部，仅调试用） |

话题常量在 `Yx.ModSdk.Hosting.Topics`（`Round`、`BattleStarted`、`BattleEnded`、`Hand`、`NetIn`、`NetOut`），`TestHost.Publish` 用得到。

```csharp
ctx.Events.OnRoundStarted(OnRound);
ctx.Events.OnNetIn<Proto.GameStatus>(OnStatus);
```

## `ctx.MainThread`

- `Post(Action)`：下一帧主线程执行，**可在任意线程调用**。
- `Delay(float seconds, Action)`：约 seconds 秒后主线程执行，可在任意线程调用。
- `ISubscription EveryFrame(Action)`：每帧执行直到 `Cancel()` 或停用。**只能在主线程调用**（和上面两个不一样）。

## `ctx.Services`

跨 mod 共享对象：`Register<T>(T impl)`、`T Get<T>()`（没有返回 null）、`bool TryGet<T>(out T)`，三个都要求 `T : class`。键是 `typeof(T).FullName`，按注册 mod 归属，停用时注销。提供方与使用方需要共享同一个类型定义（放在提供方 DLL 里并让使用方引用）。

## `ctx.Hooks`

`Prefix(typeFullName, method, paramCount, Func<HookContext, bool>)`、`Postfix(typeFullName, method, paramCount, Action<HookContext>)`；饱和算术改写 `Saturate(type)` / `Saturate(type, method, paramCount)` / `BeginSaturate(string[])` + `StepSaturate(job, instructionBudget)`；常量覆盖 `OverrideConstant(type, method, paramCount, original, fieldHint)`。Prefix / Postfix 已实机全过（实机全过）；饱和算术与常量覆盖 2026-09-19 在练习场里实机跑通了改写这一步（日志有改写处数、无报错），数值效果还在确认。见 [hooks.md](hooks.md)。

## `ctx.Guard` / `ctx.Guarded`——给不经过 SDK 的回调兜底

热键、事件、主线程任务、配置回调、钩子处理器里抛的异常，宿主都会接住、记一条 error 并计入错误预算。但 **Unity 按钮的 `onClick`、输入框的 `onEndEdit`、UniRx 订阅**这类回调是 Unity / 游戏直接调过来的：异常会漏进游戏（轻则这次点击没反应，重则打断游戏自己的流程），日志里也看不出是哪个 mod。

- `void Guard(string what, Action action)`：立刻执行，异常接住并记到本 mod 头上。
- `Action Guarded(string what, Action action)` / `Action<T> Guarded<T>(string what, Action<T> action)`：包成安全的委托再交出去，例如 `button.onClick.AddListener(new UnityAction(ctx.Guarded("点击发牌", OnDeal)))`。
- `what` 会出现在错误日志里。连续出错同样会触发错误预算（60 秒内第 5 次停用 mod）。`UiKit` 建的控件已经自动包过。`TestHost` 测试通过；游戏内待实机确认。

## `ctx.Data`

mod 自有持久化，走管理器通道：`T Get<T>(key, defaultValue)`、`Set(key, object)`、`Remove(key)`、`Has(key)`、`Keys`。值限 JSON 可表示类型（`bool`/`int`/`long`/`float`/`double`/`string`/`List<object>`/`Dictionary<string, object>`，递归校验，容器深拷贝，`Get` 返回的容器是拷贝），单 mod 序列化后 ≤ 256 KB。`Set(key, null)` 等价于 `Remove`。每次改动发 `dataChanged` 事件由管理器落盘到 `data/<id>.json`；启动 / 热重载随运行计划下发初值。JSON 下发的数值是 `double`，`Get<int>` 会转换；类型无法转换时返回默认值。`TestHost` 测试通过；游戏内读写与落盘已实机在用。

```csharp
int runs = ctx.Data.Get<int>("runs", 0);
ctx.Data.Set("runs", runs + 1);
```

## `ctx.Versions`

`Sdk`（`SdkRuntime.SdkVersion`，当前 `"1.0.0"`）、`Game`（`Application.version`，测试宿主可指定，未知为空串）。

## `Yx.ModSdk.Unity.Ui`（主线程）

- `Toast(string)`：屏幕上方描边提示，2.5 s 后自动消失，新 Toast 替换旧的。实机 PASS。
- `OutlinedLabel CreateLabel(name, anchor, pivot, position, size, fontSize, color, alignment)`：描边文本；`OutlinedLabel` 有 `SetText`/`SetVisible`/`SetPosition`/`Destroy`/`IsAlive`。找不到根 `Canvas` 返回 null。自己创建的界面对象在 `OnDisable` 里 `Destroy()`。
- `FindRootCanvas()`、`FindFont()`（优先含中文字形的 TMP 字体）。

## `Yx.ModSdk.Unity.UiKit`（主线程；待实机确认）

最小的自绘界面工具，来源是练习场 mod 里实机用稳的那套（作为 SDK 类本身还没单独实机跑过）：`new UiKit(ctx)`，然后

- `GameObject Panel(name, anchor, pivot, position, size, background, bool blockClicks)`：在游戏根 Canvas 上建一块底板；没有 Canvas（场景没加载完）返回 null。`blockClicks=false` 是纯显示的悬浮窗；不带最后一个参数的重载 = 挡点击。`Destroy(panel)` 销毁其中一块。
- 挂在面板下面的（`position` 相对父物体**左上角**，y 向下为**负**）：`Row(...)`、`TextMeshProUGUI Label(parent, name, text, position, size, fontSize)`、`UiButton TextButton(parent, name, text, position, size, Action onClick)`、`UiInput NumberInput(parent, name, position, size, maxDigits, Action<string> onEndEdit)`、`UiInput TextInput(..., maxLength, ...)`。
- `UiButton`：`SetText` / `SetInteractable` / `IsAlive` / `GameObject`。`UiInput`：`Text` / `SetText` / `Show(text)`（用户正在输入时不覆盖）/ `IsFocused`。
- 回调自动走 `ctx.Guarded`。`DestroyAll()` 销毁建过的全部面板——**在 `OnDisable` 里调**，热重载时宿主不会替你清界面。
- 示例：`examples/roundnote/src/Game/NotePanel.cs`（最小）。

## `Yx.ModSdk.Unity.SceneDetector`

`GameScene Detect()`、`int Round()`（不在对局 -1）、`int Mode()`（`(int)GameMode`，不在对局 -1）、`bool IsObserver()`（观战 / 裁判 / 回放）。已实机验证。

## GameAPI（`Yx.ModSdk.Game`，主线程）

除 `PlacementApi.HandIds()`（裸 `List<int>`，失败返回 null）外全部返回 `Result<T>`：`Ok`、`Value`（`!Ok` 时是 `default(T)`）、`Error`（失败原因，中文，给日志看的，不是稳定的错误码）。GameAPI 随游戏更新可能失效，使用前自己在游戏里验一遍。

| 方法 | 做什么 | 入口（面板 / 只读） | 验证 |
|---|---|---|---|
| `StateApi.Round()` | 当前回合 | 只读 | PASS |
| `StateApi.Self()` | `SelfState`：命元 `Life`、生命上限 `MaxHp`、体魄、境界、修为、上一回合数值 | 只读 | PASS |
| `SceneApi.Current()` | 0=修炼阶段 1=斗法阶段 | 只读 | PASS |
| `GameStatusApi.Req()` | 发 `GameStatusReq` 刷新对局状态；只在修炼阶段、非观战时 | 发包（查询） | PASS |
| `BattleApi.IsBattling()` | 是否有执行器在播战斗 | 只读 | 间接验证 |
| `BattleApi.EnableSkipButton()` / `EnableSkipButton(bool keepNormalSpeed)` | 显示跳过按钮（与游戏第 16 回合的触发相同）。游戏会同时把面板速度调到 4 倍；传 `true` 则调出后把面板的 `curSpeed` 放回 1 倍 | `BattleReplayPanel.Show` | PASS（"无动画时报错"负例未测） |
| `BattleApi.Skip()` | 显示并点跳过按钮 | `SkipAnimationButton` | PASS |
| `BattleApi.CloseSkipResult()` | 提前关掉跳过结算面板 | `SkipBattleResultPanel.Hide` | PASS |
| `PlacementApi.Board()` | **手牌**列表（`HandCard.Slot/Id`，slot 为手牌序号，不是场上格子） | 只读 | PASS |
| `PlacementApi.HandIds()` | `Board` 的 id 列表，失败返回 null | 只读 | PASS |
| `PlacementApi.Move(handIdx, gridIdx)` | 手牌摆到格子 | `CardPanel.MoveToGrid` | PASS |
| `PlacementApi.Unplace(gridIdx)` | 格子的牌收回手牌 | `CardPanel.MoveToHand` | PASS |
| `PlacementApi.Refine(handIdx)` | 炼化 | `RefineArea.RefineCard` | PASS |
| `PlacementApi.Swap(handIdx)` | 换牌；次数为 0 时返回"换牌被拒绝" | `ReplaceArea.ReplaceCard` | PASS |
| `PlacementApi.ReadyState()` | 准备按钮与预备标记：`Result<Dictionary<string, object>>`，四个 bool 键 `layerReady` / `layerInteractable` / `scrollReady` / `isPreReady` | 只读 | PASS |
| `PlacementApi.Ready()` | 点准备 | `ReadyLayer.PressReadyButton` | PASS（练习局语义；`already-ready` 未测到） |
| `PlacementApi.Breakthrough()` | 突破 | `LevelUpBtn` | PASS |
| `PlacementApi.TalentSelect(index)` | 突破后选仙命；`index < 0` 随机；93–96 需调用两次 | `TalentSelectionPanel` | PASS（随机与两步流程未测到） |
| `PlacementApi.SelectCareer(career, random, secondary)` | 选（副）职业 | 职业卡片 + `ConfirmBtn` | 部分（面板未打开与主 / 副校验正确；7 职业匹配、random 未测） |
| `PlacementApi.FateSelect(index)` | 天衍仙命 | `FateStrategyItem.toggle` + `ConfirmButton` | 未覆盖 |
| `PlacementApi.DaoyunSelect(daoYunId)` | 道韵 | 开关 + `ConfirmBtn` | 未覆盖 |
| `PlacementApi.FateBranchSelect(index)` | 命运分支 | 开关 + `ConfirmButton` | 未覆盖 |
| `PlacementApi.UiSelect(index)` | 最上层"选项 + 确定"面板选第 index 项 | `ToggleGroup` + 确定 | PASS |
| `PlacementApi.ClickButton(text)` | 点文字匹配、可交互的按钮 | `Button.onClick` | PASS |
| `NetworkApi.Analyze(pingAmount)` | 测各线路延迟（异步，结果进 `Routes`） | 只读 | PASS |
| `NetworkApi.Routes()` | 上次测速结果 `"current=i;idx:latency:loss;..."` | 只读 | PASS |
| `NetworkApi.Optimize()` | 点"优化网络"，游戏弹确认框（会离开对局） | `NetworkOptimizeButton` | PASS |

摆牌类操作共用的前置门控（`GameUi.PlacementBlocked`）：在修炼阶段、非观战、自己在对局中、摆牌区可交互、不在发牌 / 插入模式 / 拖拽中；不满足时 `Error` 说明原因。连续调用 `Move` 由游戏的动画门控拒绝（"该手牌正在播放动画"），不需要自己串行化。

```csharp
Result<int> round = StateApi.Round();
if (!round.Ok) { Context.Log.Warn(round.Error); return; }
Context.Log.Info("round " + round.Value.ToString(CultureInfo.InvariantCulture));
```
