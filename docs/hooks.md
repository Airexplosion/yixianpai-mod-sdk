# 钩子（`ctx.Hooks`）

在游戏（或 SDK）热更方法的入口 / 出口插入自己的处理器：读改参数、跳过原方法、读改返回值。机制：运行时用 Cecil 对象模型改写目标 `ILMethod` 的 IL，插入对 `Yx.ModSdk.Hooks.HookDispatch.Prefix/Postfix` 的调用，并重置 ILRuntime 的编译缓存；已经执行过的方法在下一次调用即生效（实机验证）。

## 登记

```csharp
using Yx.ModSdk.Hooks;
using Yx.Shared;

ctx.Hooks.Prefix("CardPanel", "GetHandCards", 0, OnGetHandCardsPrefix);
ctx.Hooks.Postfix("CardPanel", "GetHandCards", 0, OnGetHandCardsPostfix);
ctx.Hooks.Prefix("Yx.ModSdk.Game.PlacementApi", "Move", 2, OnMovePrefix);

bool OnGetHandCardsPrefix(HookContext h)
{
 return true; // 继续执行原方法
}

void OnGetHandCardsPostfix(HookContext h)
{
 IList list = h.Result as IList;
 string count = list == null ? "null" : list.Count.ToString(CultureInfo.InvariantCulture);
 Context.Log.Info("hand count=" + count);
}
```

- `Prefix(typeFullName, method, paramCount, Func<HookContext, bool>)` / `Postfix(typeFullName, method, paramCount, Action<HookContext>)`。
- `typeFullName` 是热更类型全名（游戏类型多数没有命名空间，如 `CardPanel`、`GameStatusExtension`；SDK 类型带命名空间）；`method` 方法名；`paramCount` 参数个数用于区分重载。三者构成站点键 `Type.Method/N`。
- 返回 `ISubscription`，`Cancel()` 移除自己的处理器；停用 / 热重载时宿主按 owner 自动移除。
- 找不到方法、拿不到 `ILMethod.def`、方法不能重写时抛 `InvalidOperationException`（消息含原因）；`OnLoad` 里没接住会让加载失败。游戏一更新方法改名是常事，所以「挂不上就少一个功能」的钩子用不抛异常的版本：
 - `ISubscription TryPrefix(...)` / `TryPostfix(...)`：参数同上；没挂上记一条 warn、返回 null。
 - `HookGroup Group(string name)`：一组要么全有、要么就别用的钩子。`group.Prefix(...)` / `group.Postfix(...)` 都不抛；装完看 `group.Complete`、`group.Missing`（形如 `CardPanel.MoveToGrid/2, …`）、`Installed` / `Failed`，`CancelAll()` 提前解除。练习场的「离线保护的钩子不全就禁止进场」就是这个模式；教学示例见 `examples/roundnote`。
 - 测试里用 `host.FailHookInstall(type, method, reason)` 模拟挂不上，测降级路径。
- 同一站点可以有多个 mod 的多个处理器，按登记顺序执行。

## `HookContext`（`Yx.Shared`）

| 成员 | 说明 |
|---|---|
| `object Instance` | 实例方法的 `this`；静态方法为 null |
| `object[] Args` | 参数的装箱副本。**前置里改 `Args[i]` 会在前置结束后写回原方法参数**（值类型改完要整个赋回去，如 `h.Args[1] = 5`）；`ref`/`out` 参数在这里是 null 且不可改写 |
| `object Result` | 后置里是原方法返回值，可改写；前置里配合 `Skip` 使用 |
| `bool Skipped` | 本次分发里是否已经调过 `Skip`。只在前置里有意义：后置拿到的是一个新的上下文，这里恒为 false（要知道原方法有没有被跳过，自己在前置里记） |
| `string Owner` | 当前处理器所属 mod id |
| `void Skip(object result)` | 跳过原方法，用 `result` 作返回值（void 方法传 null） |

## 前置的返回值与 Skip

- 返回 `true` 且未 `Skip`：继续下一个前置，最后执行原方法。
- 返回 `false` 或调用了 `Skip(result)`：原方法不执行，**其余前置不再执行**，返回 `Result`。
- **跳过后后置照常执行**（`HookDispatch.Prefix` 在跳过路径上直接跑一遍 `RunPostfix`），后置里看到的 `Result` 是 `Skip` 给的值，可以继续改写。所以后置不能假定原方法真的跑过。

## 异常隔离

处理器抛出的异常在 `HookDispatchCore` 里直接捕获，经 `ModHost.RecordError` 记 error 日志并计入该 mod 的错误预算（60 s 内 5 次熔断，见 [sdk-reference.md](sdk-reference.md)）；原方法与其他处理器不受影响。抛异常的前置不算跳过。

## 限制

- 不支持：泛型方法与泛型类型上的方法、值类型上的方法（`IlRewriter.Check` 直接拒绝）。
- `async` / 迭代器方法只能挂在**外壳**上：前置会触发（H08 `CardPanel.MoveToGrid` 验证），但后置拿不到真实的异步结果；状态机内部（`MoveNext`）暂不能挂。用 `yx.hooks.probe` 的 `isAsync` 判断。
- `ref`/`out` 参数不能改写（`Args` 里为 null）。
- 取消订阅 / 停用只移除处理器，方法体保持重写状态（处理器列表为空时分发直接返回，开销一次查表）；还原原始指令（`Restore`）尚未实现。
- 处理器在**原方法的调用线程**上执行——目前验证过的站点都在主线程；挂其它线程的方法时不要在处理器里碰 Unity 对象。
- 每帧高频方法上的性能量级还没实测（热更类型上没有 `Update`，已验证）。
- 依赖 ILRuntime / Cecil 的内部字段（`ILMethod.def/body/...`、`Instruction.opcode/operand`）。游戏更新后先跑一次 `yx.hooks.probe`，`missingFields` 非空说明钩子系统需要修。

## 饱和算术改写（`Saturate`；改写已实机跑通，数值效果确认中）

和钩子同一套机制（改写 `ILMethod` 的 IL），但改的是方法体里面的算术：确定是有符号 `int` 的 `add / sub / mul / div / neg`、`(int)浮点`、`(int)long`、`Mathf.FloorToInt / CeilToInt / RoundToInt` 换成对 `Yx.ModSdk.Hooks.SatMath` 的调用——结果超出 `int` 范围时钉在边界、不回绕。

```csharp
SaturateReport r = ctx.Hooks.Saturate("BattleCharacter"); // 整个类型（含嵌套的 async 状态机 / 闭包类）
SaturateReport one = ctx.Hooks.Saturate("BattleCharacter", "CalculateAttack", 2);

SaturateJob job = ctx.Hooks.BeginSaturate(new[] { "BattleCharacter", "CardActionBase" });
// 每帧：
if (ctx.Hooks.StepSaturate(job, 3000)) ctx.Log.Info(job.Total.Describe()); // true = 全部做完
```

- 大类型一口气改会卡几秒（分析是在解释器里跑的），用 `BeginSaturate` / `StepSaturate` 分帧：第二个参数是这一步最多处理多少条指令（至少处理一个方法）。
- 不抛异常：结果看 `SaturateReport`（`Methods` / `Sites` / `SkippedMethods` / `FirstSkip` / `Error` / `Ok` / `Describe()`）。分析不了的方法（不认识的指令、异常过滤器、栈不一致）原样不动。
- **不可撤销**，本次游戏进程内一直有效；只应该用在结果由本地决定的地方（练习场），正常对局的战斗由服务器算。
- `SatMath.HasTrue` / `TrueValue`：最近一次溢出的那条运算链的 64 位真值（启发式：之后的运算里有操作数恰好等于被钉住的边界值，就用真值接着算；回到范围内即失效）。`SatMath.Overflows` 是累计溢出次数，`ClearTrue()` 手动清掉。
- `TestHost` 里是假实现（什么都不改，`StepSaturate` 一步做完）。

## 常量覆盖（`OverrideConstant`；改写已实机跑通，效果确认中）

钩子够不着方法中间的魔法数字和 async 方法里的局部常量（编译后是状态机的字段）。`OverrideConstant` 把装载那个常量的指令改成「装载常量 → 装载槽号 → `call ConstOverrides.Get(int, int)`」：槽关着时原样返回，**改写本身不改变行为**，随时能关回去。

```csharp
ConstOverride cap = ctx.Hooks.OverrideConstant("BattleExecuter", "Execute", 3, 64, "MAX_HUI_HE_COUNT");
if (cap.Applied) cap.Set(int.MaxValue); // 下一次执行到那条指令时生效
cap.Disable(); // 关回游戏自己的 64
```

- 第 4 个参数是游戏代码里那个常量现在的值；第 5 个是字段名提示：只改紧挨着读 / 写「名字里含这段文字的字段」的那几处（`ldfld X; ldc N` 的比较、`ldc N; stfld X` 的赋值），免得误伤同一个方法里数值相同的别的常量；传 null = 方法里所有等于该值的常量。async 方法的参数和跨 await 的局部变量都是状态机的字段，名字里带原来的变量名。
- async 方法自动改它的状态机的 `MoveNext`（重载的 async 方法按桩方法的局部变量找到各自的状态机）。
- 不抛异常：`Report`（`SaturateReport`）里 `Sites` 是改到了几处，`Error` 非空 = 没做成（常量找不到多半是游戏更新了）。同一处常量重复要，拿回的是同一个槽（mod 热重载安全）。
- 改的值如果影响由服务器决定结果的流程（正常对局的战斗），用完必须 `Disable()`。

## 站点信息

`HookSite`（`Yx.Shared`）：`Id`、`Key`、`TypeName`、`MethodName`、`ParamCount`、`Applied`（已重写）、`WasCompiled`（登记时 ILRuntime 已编译过该方法）、`ApplyError`、`Calls`（前置分发次数）、`HandlerCount`、`Snapshot()`。测试里经 `TestHost.Hooks.Sites` 读取。

## 排错（RPC，经管理器 / `yx-verify` 调用）

| RPC | 参数 | 返回 |
|---|---|---|
| `yx.hooks.probe` | `[type, method, paramCount]` | `missingFields`（应为空）、`appDomain`、`found`、`error`、`compiled`、`hasDef`、`instructions`、`hasExceptionHandlers`、`isAsync`、`returnType`、`check`（`IlRewriter.Check` 的拒绝原因，空表示可挂） |
| `yx.hooks.list` | 无 | 每个站点的 `id`、`key`、`applied`、`wasCompiled`、`handlers`、`calls`、`error` |
| `yx.hooks.trace` | `[type, method, paramCount, silent]` | 以 owner `yx.trace` 挂一对前置 / 后置，把参数与返回值记成 `hook:trace` 日志（`silent=true` 只计数不记日志，用于测开销） |
| `yx.hooks.untraceAll` | 无 | 移除全部 trace 处理器，返回个数 |

典型流程：先 `probe` 确认 `found=true`、`check=""`、`isAsync=false`，再登记；登记后 `list` 看 `applied=true`、`error=""`，触发一次游戏动作后看 `calls` 是否增加。

## 在测试里驱动钩子

`TestHost` 用假安装器（只把 `Applied` 置 true），`InvokePrefix/InvokePostfix` 模拟被钩方法的调用，见 [testing.md](testing.md)。

## manifest 里声明

`provides.hooks` 列出你挂的站点（如 `"CardPanel.GetHandCards"`），管理器据此提示多个 mod 挂同一方法的软冲突（胜者为加载顺序最后者）。目前只用于提示，运行时不与登记表对账。
