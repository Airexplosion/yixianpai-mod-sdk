# `manifest.json`

mod 包根目录必须有 `manifest.json`，管理器加载前会按 schema 校验；不合法的 mod 标 ✕ 并阻止启动，错误信息形如 `entry: entry 必须是 plugins/ 下的 .dll 相对路径`。

## 最小示例（代码 mod）

```json
{
 "id": "com.example.demo",
 "name": "Demo",
 "version": "1.0.0",
 "author": "you",
 "description": "示例",
 "sdkVersion": ">=1.0.0 <2.0.0",
 "entry": "plugins/Demo.dll",
 "entryType": "Example.Demo.ModMain",
 "provides": { "hotkeys": ["F8"] }
}
```

## 字段

| 字段 | 必填 | 规则 |
|---|---|---|
| `id` | 是 | 小写反向域名：`^[a-z0-9]+([._-][a-z0-9]+)*$`，≤ 128 字符，不能是 Windows 保留设备名（`CON`、`NUL`、`COM1`…）。**不能以 `yx.` 开头**（游戏内宿主保留给 Loader / SDK，`Register` 会拒绝）。目录名与 `id` 一致 |
| `name` | 是 | 非空显示名 |
| `version` | 是 | 合法 semver（`1.2.0`） |
| `author` | 是 | 非空 |
| `description` | 否 | |
| `sdkVersion` | 代码 mod 必填 | semver range（`>=1.0.0 <2.0.0`），对照 `SdkRuntime.SdkVersion`（当前 `1.0.0`）；不满足是硬错误 |
| `gameVersion` | 否 | semver range；游戏版本号不是标准 semver 时先 coerce（`1.7.3.2` → `1.7.3`）；不满足**仅警告** |
| `entry` | 有代码时必填 | `plugins/` 下的 `.dll` 相对路径，`/` 分隔，不含 `..`、盘符、反斜杠 |
| `entryType` | 有代码时必填 | 继承 `Yx.ModSdk.YxMod` 的类的完整名（命名空间 + 类名，**不带程序集名**）；需公共无参构造函数。`yx-patch check` 的 YX016 会校验 |
| `dependencies` | 否 | `[{ "id", "version"? }]`，必需依赖；未安装 / 未启用 / 版本不符是硬错误。`version` 省略表示任意版本 |
| `optionalDependencies` | 否 | 同上结构；已启用时只影响加载顺序，缺失不报错 |
| `incompatibilities` | 否 | 同上结构；与另一已启用 mod 互相声明不兼容（任一方声明即成立）是硬错误 |
| `loadBefore` / `loadAfter` | 否 | mod id 数组，只影响顺序；目标未启用则忽略 |
| `provides` | 否 | `{ "hooks": [], "assets": [], "data": [], "hotkeys": [] }`，用于软冲突提示（见下）；缺省全空 |
| `configSchema` | 否 | 相对路径（JSON Schema 子集，管理器生成表单；04 实现） |
| `sandboxOnly` | 否 | `true` 时非 sandbox 模式启用是硬错误；默认 `false` |
| `updateUrl` | 否 | 必须是 `https://` |

`entry`、`entryType`、`sdkVersion` 三者：有任一个 `entry`/`entryType` 就必须三个都有（"代码 mod 必须声明 sdkVersion"）。没有 `entry` 的是纯资源 / 数据 mod（子项目 4/5）。

## 版本范围

`sdkVersion`、`gameVersion`、`dependencies[].version` 等用 npm semver 语法：`>=1.0.0 <2.0.0`、`^1.2.0`、`~1.2.3`、`1.x`。`satisfies(version, range)`：range 为空表示任意版本；被比较的版本先 `semver.valid`，不合法再 `semver.coerce`。

## 包布局

```
com.example.demo/
 manifest.json
 icon.png 可选
 README.md
 plugins/Demo.dll entry 指向这里
 assets/... 资源替换（子项目 4，当前只参与冲突检测）
 data/... 数据（子项目 5，当前只参与冲突检测）
```

## 解析与加载顺序

1. 只处理已启用的 mod。
2. 硬性校验（任一不过：该 mod ✕，阻止启动）：manifest 非法；必需依赖未安装 / 未启用 / 版本不符；`sdkVersion` 不满足；互相不兼容；非 sandbox 启用了 `sandboxOnly`。
3. 建图：必需依赖、已启用的可选依赖、`loadBefore`、`loadAfter`。
4. 稳定拓扑排序（Kahn）：入度为 0 的候选里选用户排序最靠前者；有环报环上节点并阻止启动。
5. 软冲突（警告，不阻止）：同一 hook / 资源路径 / 数据键 / 热键被多个已启用 mod 占用，**胜者为加载顺序最后者**。

## `provides` 与软冲突

| 键 | 内容 | 匹配 |
|---|---|---|
| `hooks` | 你挂的站点名，如 `"CardPanel.GetHandCards"` | 字符串相等 |
| `hotkeys` | 默认热键，如 `"F8"`、`"Ctrl+Shift+D"` | 归一化后比较（大小写不敏感，修饰键顺序 CTRL、SHIFT、ALT） |
| `assets` | 资源路径或 glob（`"cards/*"`） | glob 会命中其它 mod 的具体文件；`assets/` 目录下的实际文件也自动参与 |
| `data` | 数据键（`"card:1000001.desc"`） | 字符串相等；`data/` 目录下的文件按 `file:<路径>` 参与 |

声明是自愿的，只用于管理器提示；运行时的热键冲突另有游戏内检测（`ctx.Input.RegisterHotkey` 返回 false + `hotkeyConflict` 事件），钩子站点目前不与 `provides.hooks` 对账。

## 运行时拿到的信息

游戏内 Loader 只收到运行计划里的 `id`、`version`、`entryType`、`config`、`data`（`RuntimeModPlan`）；`ctx.ModId` / `ctx.Version` 即来自这里。`name`、`author`、`description` 只在管理器界面使用。
