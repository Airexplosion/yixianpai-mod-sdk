using System.Globalization;
using Yx.ModSdk;

namespace MyMod
{
    /// <summary>
    /// 入口类。宿主在 OnLoad 之后才会调用其余回调；停用/热重载时订阅、热键、主线程任务由宿主自动清理，不必手动 Cancel。
    /// 这里只用 Yx.ModSdk.Core 的 API（ctx.Log / Config / Input / Events ...），所以 tests/ 能用 TestHost 在纯 .NET 下驱动；
    /// Toast、GameAPI 等属于游戏绑定层（Yx.ModSdk.dll 的 Yx.ModSdk.Unity / Yx.ModSdk.Game 命名空间），只能在游戏里跑，
    /// 写在条件编译块（符号 YX_GAME）里：仓库根有 refs/ 时才会定义这个符号（没有 refs/ 也能构建和测试，只是这些代码不编进去）。
    /// 写法受 ILRuntime 约束（见 docs/ilruntime-rules.md）：数字拼接用 ToString(CultureInfo.InvariantCulture)，不用 LINQ / 字符串插值。
    /// </summary>
    public sealed class ModMain : YxMod
    {
        ConfigEntry<int> _greetCount;

        public override void OnLoad(ModContext ctx)
        {
            // ctx 与 Context 是同一个对象：OnLoad 里用参数，其他回调里用 Context。
            _greetCount = ctx.Config.Bind("general", "greetCount", 1, "按热键时打招呼的次数");
            // 返回 false = 这个键和别的 mod / 面板热键冲突了，热键暂不生效（用户可以在管理器里改键）。
            if (!ctx.Input.RegisterHotkey("greet", "F9", Greet)) ctx.Log.Warn("热键 F9 被占用，到管理器里给「greet」换一个键");
            ctx.Events.OnRoundStarted(OnRound);
            ctx.Log.Info("我的 Mod 已加载，SDK " + ctx.Versions.Sdk + "，游戏 " + ctx.Versions.Game);
        }

        void Greet()
        {
            int n = _greetCount.Value;
            for (int i = 0; i < n; i++) Context.Log.Info("你好，弈仙牌！");
#if YX_GAME
            Yx.ModSdk.Unity.Ui.Toast("你好，弈仙牌！");      // 游戏绑定层：只在游戏里可用，TestHost 下不编进来
#endif
            Context.Log.Info("打招呼 x" + n.ToString(CultureInfo.InvariantCulture));
        }

        void OnRound(int round)
        {
            Context.Log.Debug("回合 " + round.ToString(CultureInfo.InvariantCulture));
        }
    }
}
