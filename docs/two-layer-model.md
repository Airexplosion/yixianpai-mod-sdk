# 两层模型：推荐层与逃生舱

mod 代码和 SDK、Loader 一起运行在游戏的 ILRuntime 热更域 `DarkSun.HotUpdate` 里，因此**技术上**mod 可以直接引用游戏的任何热更类型。SDK 把这件事分成两层，责任不同。

## 程序集

| 程序集 | 目标框架 | 内容 | 谁引用 |
|---|---|---|---|
| `Yx.ModSdk.Core` | net40 + netstandard2.0 | 只依赖 BCL：`YxMod`、`ModContext` 全部部件、`SdkRuntime`、`HookDispatch`、`Yx.Shared` 逻辑 | mod 工程、测试工程 |
| `Yx.ModSdk` | net40 | 游戏绑定：`Yx.ModSdk.Game.*`、`Yx.ModSdk.Unity.*`、RPC、IL 重写器、`GameRuntime.Install()` | mod 工程 |
| `Yx.ModSdk.Testing` | netstandard2.0 | `TestHost` 内存宿主 | 测试工程 |

加载顺序 Core → ModSdk → ModLoader → mods。三程序集拆分在 已验证 完成，测试通过；拆分后的游戏内加载（待实机确认，已验证）。

## 推荐层

由 SDK 维护、经过逆向审计与实机验证（各方法的验证状态见 `sdk-reference.md` 的 GameAPI 表，fate/daoyun/fateBranch 与 selectCareer 部分未覆盖）、跨游戏版本由我们负责修的接口：

- **`ctx.*`**：日志、配置、热键、事件、主线程队列、服务、数据、版本（[sdk-reference.md](sdk-reference.md)）。
- **GameAPI**：`Yx.ModSdk.Game` 下的静态类 `StateApi`、`BattleApi`、`SceneApi`、`GameStatusApi`、`NetworkApi`、`PlacementApi`，一律返回 `Result<T>`（`Ok` / `Value` / `Error`），不抛异常。
- **Unity 工具**：`Yx.ModSdk.Unity.Ui`（`Toast`、`CreateLabel` 描边文本）、`SceneDetector`。
- **事件**：`ctx.Events.OnRoundStarted/OnBattleStarted/OnBattleEnded/OnHandChanged/OnNetIn<T>/OnNetOut<T>`。
- **钩子**：`ctx.Hooks.Prefix/Postfix`（[hooks.md](hooks.md)）。

推荐层的**绑定规则**（用户明确要求，SDK 自身遵守）：

1. **改变游戏状态的操作一律走游戏自己的面板 / 按钮 / 开关**——与玩家点击或拖拽同一入口（如 `PlacementApi.Move` 调的是 `CardGrid.OnDrop` 调的那个 `CardPanel.MoveToGrid`；`PlacementApi.Ready` 调 `ReadyLayer.PressReadyButton`）。
2. **发协议包只用于无副作用的取值 / 刷新**（`GameStatusApi.Req` 发的 `GameStatusReq` 是查询）。找不到面板路径的状态操作 SDK 不提供，也不用发包兜底。
3. 战斗"提前跳过"走游戏自己的跳过流程（`BattleApi.EnableSkipButton` → `Skip` → `CloseSkipResult`），不改 `Time.timeScale`、不打断执行器、不切场景。
4. 面板类 API 的"成功"以游戏的记账为准（`Swap`/`Refine` 看 `CardItem.dropTarget` 是否被游戏设为目标区域），不以"没抛异常"当成功。

## 逃生舱

mod 工程引用了 `refs/DarkSun.HotUpdate.dll`，所以可以直接写 `BattleManager.Instance.currentGameStatus`、`ILRPanelBase.FindILRPanel<BattlePanel>()` 这类代码。这是允许的，但：

- **随游戏更新随时断**。游戏更新很频繁，热更 DLL 的成员会改名、改签名、消失；SDK 不替你适配，也不保证你用到的类型下个版本还在。出错的表现通常是 `OnLoad` 抛异常导致 mod 状态变为 `failed`，或者运行中连续出错触发错误预算被自动停用（[sdk-reference.md](sdk-reference.md)"错误预算"）。
- **必须自己遵守上面的绑定规则**。直接调用 `SimpleClientPact` / `SelectCareerReq` 之类的发包改状态，是这个项目明确禁止的做法；SDK 的审计删掉了 `ForceBreak`、`SceneApi.Change`、`NetworkApi.AutoSelect`、`Pact` 就是这个原因。
- **`yx-patch check` 不检查游戏 API 用法**。它只扫 ILRuntime 语言层面的陷阱（LINQ、装箱拼接、委托进 CLR 集合……），不知道你调的是面板还是发包，也不知道那个成员在当前游戏版本里是否存在。
- 逃生舱代码仍然受 ILRuntime 约束（[ilruntime-rules.md](ilruntime-rules.md)）：不能对热更 enum 调 `ToString()`、不能把委托塞进 `List<Action>`、泛型方法闭包里不能对 T 强转，等等。继承 `DarkSun.HotUpdate` 里的类型是允许的（`check` 的 YX011 把它与 `Yx.*` 同等对待），继承 `DarkSun.UI` / `DarkSun.Utility` / `UnityEngine.*` 的类型仍是 error。
- 游戏 / Unity 的 DLL 与反编译代码是厂商版权材料，只放本机 `refs/`，永不提交、永不分发；文档和 issue 里只引用成员签名和极短片段。

## 什么时候用哪层

| 需求 | 用 |
|---|---|
| 读回合、自身数值、手牌 | `StateApi.Round/Self`、`PlacementApi.Board`（注意：返回的是**手牌**列表，slot 为手牌序号） |
| 摆牌 / 收回 / 炼化 / 换牌 / 准备 / 突破 / 选项面板 | `PlacementApi.*` |
| 提前跳过战斗 | `BattleApi.EnableSkipButton/Skip/CloseSkipResult` |
| 在某个游戏方法前后做事、改参数、改返回值 | `ctx.Hooks`（[hooks.md](hooks.md)） |
| 知道进了哪个场景 / 回合开始 / 手牌变化 | `OnSceneChanged`、`ctx.Events.*` |
| 收协议消息 | `ctx.Events.OnNetIn<Proto.XXX>` |
| 上面都没有 | 逃生舱：直接引用热更类型（需要 `refs/`），自己在游戏里验证 |

改游戏状态时优先走游戏自己的面板 / 按钮 / 开关（和玩家点击同一个入口）；发协议包只用于无副作用的取值 / 刷新。找不到面板路径的状态操作，宁可不做，也别用发包硬来——游戏一更新就崩，还可能触发异常行为。

## GameAPI 的形态

GameAPI 是 `Yx.ModSdk.Game` 下的**静态类**，界面工具是 `Yx.ModSdk.Unity` 下的静态类（`ModContext` 上没有 `Game` / `Ui` 属性）。这两块都属于游戏绑定层（`Yx.ModSdk.dll`），只在有 `refs/` 且定义了 `YX_GAME` 时可用。
