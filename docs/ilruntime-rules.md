# ILRuntime 编码约束

mod 在 ILRuntime 解释执行（C# 7.3、net40），很多在普通 .NET 里正常的写法会**静默出错**——不抛异常、没有日志、只是结果不对。`yx-patch check <dll>` 从 IL 层扫描下面这些规则（规则号 YX001–YX018）；error 阻止部署（退出码 1），warning 需要人工判断（退出码 0，`--warn-as-error` 时 1）。输出一行一条：`error YX001 Ns.Type::Member: 说明`。命令用法见 [README.md](README.md) 第 6 步。

工程设置：`<TargetFramework>net40</TargetFramework>`、`<LangVersion>7.3</LangVersion>`（模板已设好）。

## YX001 LINQ（error）

- 错误写法：`hand.Where(c => c.Id > 0).ToList()`、`using System.Linq;`
- 表现：ILRuntime 对 `Enumerable` 扩展方法的委托适配不完整，轻则异常重则静默空结果。
- 正确写法：`for` 循环。

## YX002 字符串格式化与装箱拼接（error）

- 错误写法：`"count=" + count`（int 隐式装箱）、`$"count={count}"`、`string.Format("{0}", x)`、`sb.AppendFormat(...)`。
- 表现：spike 实测复合拼接返回被截断的字符串（`"sdk frames="` 后面全丢），不抛异常。
- 正确写法：每个值先显式转成 `string` 局部变量，再只拼 string：

```csharp
string countText = count.ToString(CultureInfo.InvariantCulture);
string enabledText = enabled ? "true" : "false";
ctx.Log.Info("count=" + countText + " enabled=" + enabledText);
```

不要在一个拼接表达式里混用字面量、装箱数字与三元表达式（三元混拼 IL 层无法区分，`check` 不报，靠自觉）。注意现代 Roslyn 会把 `"n=" + n` 编译成 `n.ToString()` + `Concat(string, string)`，这种形态 YX002 看不到，由 YX017 补上。

## YX003 迭代器（error）

- 错误写法：`IEnumerator<int> Walk() { yield return 1; }`
- 表现：编译器生成的状态机实现 CLR 接口 `IEnumerator`，ILRuntime 下不可靠。
- 正确写法：返回 `List<T>`，或用回调。

## YX004 async / await（error）

- 错误写法：`async void OnLoad`、引用 `Task`/`Task<T>`/`ValueTask`。
- 正确写法：`ctx.MainThread.Delay(seconds, action)` / `Post(action)` 做延时与投递。

## YX005 dynamic（error）

- 错误写法：`dynamic x = ...`（引用 `Microsoft.CSharp.RuntimeBinder`）。
- 正确写法：显式类型。

## YX006 三参数 Enum.Parse（error）

- 错误写法：`Enum.Parse(typeof(Greeting), text, true)`
- 表现：ILRuntime 不重定向三参数重载。
- 正确写法：两参数版本，忽略大小写自己先按 `Enum.GetNames` 匹配（SDK 内部 `EnumConfig.TryParse` 就是这么做的）：

```csharp
string[] names = Enum.GetNames(typeof(Greeting));
for (int i = 0; i < names.Length; i++)
{
 if (string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
 return (Greeting)Enum.Parse(typeof(Greeting), names[i]);
}
```

## YX007 自定义 enum 的 ToString（warning）

- 错误写法：`greeting.ToString()`、`"scene " + scene`（隐式调用 ToString）。
- 表现：热更定义的 enum 经 `constrained. callvirt Object::ToString()` 走到 ILRuntime 的值类型绑定，结果不可靠。
- 范围：被检 DLL 自己定义的 enum，以及 `Yx.*` 程序集（SDK 也是热更代码）里的**任何值类型**——checker 不解析外部程序集，分不出外部的 enum 与结构体，所以外部值类型一律按值类型报。
- 正确写法：自己的名字表（`switch` 返回字符串），或 `((int)scene).ToString(CultureInfo.InvariantCulture)`。hello 示例的 `SceneName(GameScene)` 是范例。

## YX008 委托进 CLR 泛型集合（error）

- 错误写法：`List<Action<int>>`、`Dictionary<string, Action>`、`list.Sort((a, b) => ...)`、`list.Find(x => ...)`、`list.RemoveAll(...)`、`Array.Sort(arr, cmp)`。
- 表现：热更委托放进 CLR 泛型实例化需要游戏没注册的委托适配器，运行时异常或静默丢失。
- 正确写法：**持有类**——自己（热更）代码里的类包住委托，集合里存持有类：

```csharp
sealed class Handler
{
 public Action<int> Fn;
}

readonly List<Handler> _handlers = new List<Handler>();

void Add(Action<int> fn)
{
 var h = new Handler();
 h.Fn = fn;
 _handlers.Add(h);
}
```

SDK 内部的 `ValueHandler<T>`、`ConfigRaiser`、`HookHandler` 都是这个模式。排序 / 查找自己写循环。跨到 CLR 的委托目前只允许 UniRx 的 `Action<long>`（游戏还注册了 `Action<int>`/`Action<string>`/`Action<bool>`/`Func<bool>` 等，但未逐个实测，先不要依赖）。

## YX009 泛型方法的闭包里对 T 强转（error）

- 错误写法：

```csharp
ISubscription OnMessage<T>(Action<T> handler) where T : class
{
 return bus.Subscribe(topic, p => { T m = p as T; if (m != null) handler(m); }); // 错
}
```

- 表现：`as T` 在 ILRuntime 下**静默返回 null**，回调永远不触发，没有任何日志（实测）。
- 正确写法：用**泛型类的实例方法**承载转换，泛型类里的 `(T)x` 强转已实机验证：

```csharp
sealed class TypedHandler<T> where T : class
{
 readonly Action<T> _fn;
 public TypedHandler(Action<T> fn) { _fn = fn; }
 public void Invoke(object p)
 {
 if (p == null) return;
 T m;
 try { m = (T)p; }
 catch (InvalidCastException) { return; }
 _fn(m);
 }
}

ISubscription OnMessage<T>(Action<T> handler) where T : class
{
 var typed = new TypedHandler<T>(handler);
 return bus.Subscribe(topic, typed.Invoke);
}
```

SDK 的 `NetHandler<T>`、`DataConvert<T>` 就是这样写的。

## YX010 泛型方法体内对 T 强转（warning）

- 写法：非闭包的泛型方法体里 `(T)x` / `x as T` / `x is T`。
- 说明：只有泛型**类**里的强转经过实机验证；方法级泛型参数的强转没测过。建议同 YX009 改成泛型类。`typeof(T).IsInstanceOfType(x)` 后再 `(T)x`（`DataConvert<T>.From` 的写法）是已验证的组合。

## YX011 实现 CLR 接口 / 继承 CLR 类（error）

- 错误写法：`sealed class MySub : IDisposable`、`class MyList : List<int>`、`class MyBehaviour : MonoBehaviour`。
- 表现：热更类型实现 CLR 接口需要 ILRuntime 的跨域适配器，游戏没注册就失败。
- 正确写法：用 SDK 的 `Yx.Shared.ISubscription`（`Cancel()` / `IsActive`）代替 `IDisposable`；组合而不是继承。
- 允许的基类 / 接口：本 DLL 自己的；以 `Yx.` 开头的程序集里的（`YxMod`、`ISubscription`）；**`DarkSun.HotUpdate` 里的**（它本身就是 ILRuntime 热更代码，继承其中的类型不跨域）；白名单 `Object`、`Attribute`、`Enum`、`ValueType`、`MulticastDelegate`、`Exception`（可以继承 `Exception`）。`DarkSun.UI`、`DarkSun.Utility`、`UnityEngine.*`、`UniRx` 等仍是 CLR 程序集，继承 / 实现它们报 error；确有必要用 `--allow-base <类型全名>` 放行。

## YX012 接口上的泛型方法（error）

- 错误写法：`interface IStore { T Get<T>(string key); }`
- 正确写法：把泛型放到类上（`Store<T>`），或用非泛型签名返回 `object`。

## YX013 目标框架（error）

- 表现：引用了 `netstandard` / `System.Runtime`，或 `mscorlib` 主版本不是 4——不是 net40 编译。
- 正确写法：`TargetFramework` 为 `net40`；不要引用 `Microsoft.Bcl.*`。

## YX014 值类型参数的 CLR 泛型集合（warning）

- 写法：`List<int>`、`Dictionary<int, MyStruct>`、`HashSet<float>`；`Queue<T>`/`Stack<T>`/`LinkedList<T>` 同样检查。
- 说明：IL2CPP 的 AOT 可能没有对应实例化。`List<int>`、`Dictionary<int, int>`、`Dictionary<string, int>` 在游戏和 SDK 里已大量使用没有问题；自定义结构体 / 枚举做泛型参数则尽量避免，改用 `object` 或引用类型包装。

## YX015 反射（warning）

- 写法：`Type.GetType`、`Activator.CreateInstance`、`MethodInfo.Invoke`、`ConstructorInfo.Invoke`、`Assembly.Load*`。
- 说明：反射只在设计要求处使用（Loader 按 `entryType` 实例化、钩子系统访问 `ILMethod` 字段）。mod 里出现请说明理由。

## YX016 入口类型（error）

DLL 同目录或上一级目录有 `manifest.json`（或 `--manifest <path>`）时检查：`entryType` 在 DLL 里存在、继承 `Yx.ModSdk.YxMod`、有公共无参构造函数。三者之一不满足，游戏内 `SdkRuntime.CreateInstance` 会抛 `InvalidOperationException`，mod 状态 `failed`。

## YX018 字符串常量里的控制字符（warning）

字符串常量里出现 0x00–0x08 的字符。几乎总是意外：源码里写的转义序列（尤其是"反斜杠加 u"）在写文件时被工具变成了真实的控制字符，编译器照单全收，运行时界面上就是一个方块或者直接截断。需要控制字符时写 `(char)10` 这种形式（换行、制表符不在本规则范围内）。

## YX017 数字的无 culture ToString（warning）

- 错误写法：`"n=" + n`（`n` 为 `int`/`float` 等）、`n.ToString()`。
- 表现：现代 Roslyn 把 `"n=" + n` 编译成 `n.ToString()` + `String.Concat(string, string)`，不经过 YX002 盯的装箱重载；`ToString()` 走当前 culture，ILRuntime 对值类型无参 `ToString` 的重定向也不在已验证范围内。
- 正确写法：`n.ToString(CultureInfo.InvariantCulture)`（实机验证的写法）。

## `check` 不覆盖、靠自觉的

- **反斜杠加 u 的转义**：源码里不要写反斜杠加 u（Unicode 转义）（写文件工具会把它变成真实控制字符，编译器报"意外字符"）。需要控制字符用 `(char)10`。提交前：`python -c "import glob;print([f for f in glob.glob('src/**/*.cs',recursive=True) if any(b<9 for b in open(f,'rb').read())])"` 应输出 `[]`。
- **三元表达式混拼**：见 YX002。
- **主线程**：`ctx.*` 除 `MainThread.Post/Delay` 外都只能在主线程调用；`OnLoad` 等回调本身在主线程。
- **不要缓存 `HookContext.Args` 之外的装箱值再改**：见 [hooks.md](hooks.md)。

## 已实机验证的模式一览

| 模式 | 验证 |
|---|---|
| 持有类包委托（`ValueHandler<T>`、`ConfigRaiser`、`HookHandler`） | 已验证 |
| 泛型类实例方法承载 `(T)x`（`NetHandler<T>`、`ConfigEntry<T>`） | 已验证 |
| `ToString(CultureInfo.InvariantCulture)` + 纯字符串拼接 | 已验证 |
| 两参数 `Enum.Parse`、`Enum.GetName`、`Enum.GetNames` | 已验证 |
| `ISubscription` 代替 `IDisposable` | 已验证（软停用清理） |
| `Type.GetType` + `Activator.CreateInstance`（Loader 侧） | 已验证 |
| `double[]` 环形缓冲代替 `Queue<double>`（`ErrorBudget`） | 已验证 |

## 校准：SDK 自己过不过

你的 mod 应以 **0 error** 为硬标准（warning 可按下面几类逐条判断是否可接受）：

| warning | SDK 里的例子 | 什么时候可接受 |
|---|---|---|
| YX014 | `List<int>`（手牌 id）、`Dictionary<string, int>`（键码缓存）、读游戏字段的 `Dictionary<int, int>` / `List<Proto.FateBranchType>` | `int`/`string` 参数的 `List`/`Dictionary` 是 AOT 必有的实例化；类型是游戏自己字段的声明类型。自定义结构体 / 枚举做泛型参数则要改 |
| YX015 | `SdkRuntime.CreateInstance` 的 `Type.GetType`、`IlEmit` 的 `ConstructorInfo.Invoke` | 这两处是设计要求的反射点。mod 里出现请说明理由 |
| YX010 | `ModServices.Get<T>` 方法体内的 `as T` | 非闭包的泛型方法体内强转实机里走通过，但没有单独验证过；优先改成泛型类实例方法 |

SDK 没有 YX017 命中：数字拼接都带了 `InvariantCulture`。

## 还没有对应检查规则的实机坑

- **`new string(char, int)` 调不了**（实测）：写在入口类的静态字段初始化里，结果静态构造函数抛异常、管理器报「创建实例失败：ILRuntimeException … at <入口类>..cctor()」，整个 mod 起不来。要重复字符用 `StringBuilder.Append(char)`。推论：**入口类（以及 `OnLoad` 会碰到的类）的静态字段初始化只放确定能跑的东西**——字面量、数组字面量、`new Color(...)` 这类已经实机跑过的；拿不准的放进方法里，出了错至少只坏一个功能，日志里也有上下文。
