# 回合小记（roundnote）——教学示例

记下你打了多少回合、多少场斗法、这场斗法双方出手多少次；按 **F7** 在右上角开关一个小面板（同时把数字写进日志）。功能很小，代码是按「给人读」写的：一个正经 mod 会用到的东西各走了一遍。

**它是「纯 Core / `#if YX_GAME` 分层」的范本**：纯逻辑（计数、存档、配置、事件）不碰游戏，零配置就能编和测；面板那部分写在 `#if YX_GAME` 里，有 `refs/` 才编进去。

| 想学什么 | 看哪里 |
|---|---|
| 入口类怎么写、生命周期 | `src/RoundNoteMod.cs`（从上往下读，编号 1–5 的注释） |
| 配置项（管理器里自动出表单）与 `OnChanged` | `OnLoad` 的第 2 步、`OnCountTurnsChanged` |
| 热键，以及键被占用怎么办 | `OnLoad` 的第 3 步 |
| 事件：回合开始、斗法开始 / 结束 | `OnLoad` 的第 4 步 |
| 存档 `ctx.Data` | `OnLoad` 的第 1 步、`OnRound` |
| 钩子组：挂不上只少一个数字，mod 照常工作 | `InstallTurnHooks`、`OnTurnStarted` |
| 把逻辑写成不碰游戏的纯类 | `src/RoundStats.cs` |
| 自绘界面（`UiKit`：面板、文字、按钮，回调自动兜底） | `src/Game/NotePanel.cs`（`#if YX_GAME`） |
| 游戏专属代码怎么和可测试的代码分开 | `#if YX_GAME`：主工程定义了它，测试工程没有 |
| 用 `TestHost` 测 mod（含「钩子挂不上」的降级路径） | `tests/RoundNoteTests.cs` |
| ILRuntime 下的写法（数字转字符串、换行符） | `RoundStats.Describe` 的注释 |

## 编 / 测 / 打包

```bash
# 纯逻辑测试：不需要游戏、不需要 refs/
dotnet test examples/roundnote/tests/RoundNote.Tests.csproj

# 编译：没有 refs/ 就按纯 Core 编（面板不编进去）；有 refs/ 自动带上面板
dotnet build examples/roundnote/RoundNote.csproj -c Release

# 校验 + 打包 → dist/com.yx.roundnote-<version>.zip
tools/yx-patch pack examples/roundnote
```

要看面板的完整效果：先在仓库根跑 `tools/yx-patch refs --game "<你的游戏目录>" --out refs` 生成 refs/，再重编、装进游戏。
