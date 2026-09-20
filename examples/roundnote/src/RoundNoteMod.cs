using System.Globalization;
using Yx.ModSdk;
using Yx.ModSdk.Hooks;
using Yx.Shared;

namespace YxRoundNote
{
    /// <summary>
    /// 教学示例「回合小记」：记下你打了多少回合、多少场斗法，按 F7 看一眼。它小，但一个正经 mod 会用到的东西都走了一遍：
    ///
    ///   配置          ctx.Config.Bind（管理器里会自动生成表单）+ OnChanged
    ///   热键          ctx.Input.RegisterHotkey，并检查「键被占用」
    ///   事件          ctx.Events.OnRoundStarted / OnBattleStarted / OnBattleEnded
    ///   存档          ctx.Data（历史累计回合数，跨启动保留）
    ///   钩子          ctx.Hooks.Group：挂不上只少一个数字，不让整个 mod 挂掉
    ///   界面          Yx.ModSdk.Unity.UiKit（只在游戏里有，见 Game/NotePanel.cs）
    ///   测试          tests/ 用 TestHost 在纯 .NET 里驱动这个类
    ///
    /// 分层：这个文件只用 Yx.ModSdk.Core 的 API，所以测试工程能直接把它编进去。碰 Unity / 游戏的代码放在 #if YX_GAME 里
    /// （工程文件里定义了这个符号，测试工程没有定义）——和 dotnet new yxmod 模板的约定一样。
    /// </summary>
    public sealed class RoundNoteMod : YxMod
    {
        const string LifetimeKey = "lifetimeRounds";

        readonly RoundStats _stats = new RoundStats();
        ConfigEntry<bool> _announce;
        ConfigEntry<bool> _countTurns;
        HookGroup _turnHooks;
#if YX_GAME
        NotePanel _panel;
#endif

        /// <summary>给测试看的：统计数字。</summary>
        public RoundStats Stats { get { return _stats; } }

        public override void OnLoad(ModContext ctx)
        {
            // 1. 存档：上次累计到多少。Get 的第二个参数是没有这个键（或类型对不上）时的默认值。
            _stats.LifetimeRounds = ctx.Data.Get<int>(LifetimeKey, 0);

            // 2. 配置：section / key / 默认值 / 说明（说明会显示在管理器的配置表单里）。
            _announce = ctx.Config.Bind("general", "announce", true, "每回合开始时在日志里报一下回合数");
            _countTurns = ctx.Config.Bind("general", "countTurns", true, "统计斗法里的出手次数（要挂一个游戏钩子）");
            _countTurns.OnChanged(OnCountTurnsChanged);

            // 3. 热键：返回 false = 这个键和别的 mod 冲突了，暂不生效（用户可以在管理器里改键）。
            if (!ctx.Input.RegisterHotkey("toggle", "F7", Toggle)) ctx.Log.Warn("热键 F7 被占用，到管理器里给「toggle」换一个键");

            // 4. 事件：订阅不用自己取消，mod 停用 / 热重载时宿主会清理。
            ctx.Events.OnRoundStarted(OnRound);
            ctx.Events.OnBattleStarted(OnBattleStarted);
            ctx.Events.OnBattleEnded(OnBattleEnded);

            // 5. 钩子：游戏一更新，方法改名是常事。用钩子组登记——挂不上只记一条 warn，不抛异常。
            if (_countTurns.Value) InstallTurnHooks();

            ctx.Log.Info("回合小记已加载，历史累计 " + N(_stats.LifetimeRounds) + " 回合");
        }

        public override void OnDisable()
        {
            // 订阅、热键、钩子处理器由宿主自动清理；自己建的界面物体要自己销毁。
#if YX_GAME
            if (_panel != null) _panel.Destroy();
#endif
        }

        void InstallTurnHooks()
        {
            if (_turnHooks != null) return;
            _turnHooks = Context.Hooks.Group("出手统计");
            // BattleCharacter.OnTurnStarted()：斗法里每个角色每次轮到自己出手时调一次。
            _turnHooks.Prefix("BattleCharacter", "OnTurnStarted", 0, OnTurnStarted);
            if (!_turnHooks.Complete) Context.Log.Warn("出手次数统计不可用（没挂上：" + _turnHooks.Missing + "），其余功能照常");
        }

        void OnCountTurnsChanged(bool enabled)
        {
            if (enabled) { InstallTurnHooks(); return; }
            if (_turnHooks != null) _turnHooks.CancelAll();
            _turnHooks = null;
        }

        // 前置钩子的返回值：true = 继续执行游戏自己的方法。这里只是数数，永远放行。
        bool OnTurnStarted(HookContext h)
        {
            _stats.OnTurn();
            return true;
        }

        void OnRound(int round)
        {
            _stats.OnRound(round);
            // 每次改动都会由管理器落盘；值只能是 JSON 能表示的类型。
            Context.Data.Set(LifetimeKey, _stats.LifetimeRounds);
            if (_announce.Value) Context.Log.Info("第 " + N(round) + " 回合（历史累计 " + N(_stats.LifetimeRounds) + "）");
            Refresh();
        }

        void OnBattleStarted()
        {
            _stats.OnBattleStarted();
            Refresh();
        }

        void OnBattleEnded()
        {
            _stats.OnBattleEnded();
            Context.Log.Info("斗法结束：这场双方共出手 " + N(_stats.TurnsThisBattle) + " 次");
            Refresh();
        }

        void Toggle()
        {
            Context.Log.Info(_stats.Describe().Replace((char)10, ' '));
#if YX_GAME
            if (_panel == null) _panel = new NotePanel(Context, OnResetClicked);
            _panel.Toggle(_stats.Describe());
#endif
        }

        void OnResetClicked()
        {
            _stats.ResetSession();
            Refresh();
        }

        void Refresh()
        {
#if YX_GAME
            if (_panel != null) _panel.Show(_stats.Describe());
#endif
        }

        static string N(int value) { return value.ToString(CultureInfo.InvariantCulture); }
    }
}
