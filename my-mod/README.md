# my-mod —— 起步模板

复制这个目录改个名，就是你自己的 mod 的起点。纯 Core,零配置就能编和测。

## 改名清单

复制 `my-mod/` 成 `你的-mod/` 之后,改这几处:

1. `manifest.json` 的 `id`(反向域名,如 `com.你的名.某功能`)、`name`、`author`、`description`、`entry`（`plugins/你的.dll`）、`entryType`（`你的命名空间.ModMain`）。
2. `MyMod.csproj` 的 `AssemblyName` 和 `RootNamespace`。
3. `src/ModMain.cs` 的 `namespace`。
4. `tests/MyMod.Tests.csproj` 里没有硬编码名字,不用改;`tests/ModMainTests.cs` 里的 `const string Id` 和 `using` 的命名空间跟着改。

## 编 / 测 / 打包

```bash
dotnet build MyMod.csproj -c Release      # 产物 → plugins/MyMod.dll
dotnet test  tests/MyMod.Tests.csproj     # 纯 .NET 下用 TestHost 驱动
../tools/yx-patch check plugins/MyMod.dll # ILRuntime 约束校验(必须 0 error)
../tools/yx-patch pack .                  # 打包 → dist/<id>-<version>.zip
```

（在仓库根用相对路径 `tools/yx-patch`；在本目录里用 `../tools/yx-patch`。）

## 要碰游戏(Toast / 面板 / GameAPI / Hook)

那些代码写在 `#if YX_GAME ... #endif` 里(见 `src/ModMain.cs` 的例子)。要让它们编进去,先在仓库根生成 `refs/`:

```bash
tools/yx-patch refs --game "<你的弈仙牌安装目录>" --out refs
```

之后同一条 `dotnet build` 会自动带上 `YX_GAME`。详见根目录 [docs/getting-started.md](../docs/getting-started.md)。
