using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Yx.ModSdk;
using Yx.ModSdk.Game;
using Yx.ModSdk.Hooks;
using Yx.ModSdk.Rpc;
using Yx.ModSdk.Unity;
using Yx.Shared;

namespace YxHello
{
    public enum Greeting
    {
        Hello = 0,
        Welcome = 1
    }

    public sealed class HelloService
    {
        public string Name() { return "hello-service"; }
    }

    /// <summary>SDK 示例：每个功能都打一条可检索的日志，供实机冒烟测试判定。</summary>
    public sealed class HelloMod : YxMod
    {
        ConfigEntry<bool> _enabled;
        ConfigEntry<int> _count;
        ConfigEntry<Greeting> _greeting;
        ISubscription _frameTask;
        int _frames;
        int _updates;
        int _netIn;
        ConfigEntry<bool> _hooksEnabled;
        ConfigEntry<int> _redirectGrid;
        ConfigEntry<bool> _skipBoard;
        ConfigEntry<bool> _throwOnce;
        ConfigEntry<bool> _throwInEvent;
        ConfigEntry<bool> _throwCaught;
        ConfigEntry<bool> _throwInSelfIsPlayer;
        bool _hooksInstalled;

        public override void OnLoad(ModContext ctx)
        {
            ctx.Log.Info("hello:load stamp=" + BuildStamp.Value);
            ctx.Log.Info("hello:format " + RpcDispatcher.FormatProbe());

            int runs = ctx.Data.Get<int>("runs", 0);
            ctx.Data.Set("runs", runs + 1);
            ctx.Log.Info("hello:data runs=" + (runs + 1).ToString(CultureInfo.InvariantCulture));
            ctx.Log.Info("hello:versions sdk=" + ctx.Versions.Sdk + " game=" + ctx.Versions.Game);

            _enabled = ctx.Config.Bind("general", "enabled", true, "是否在进入游戏时显示问候");
            _count = ctx.Config.Bind("general", "count", 3, "问候次数");
            _greeting = ctx.Config.Bind("general", "greeting", Greeting.Welcome, "问候语");
            string enabledText = _enabled.Value ? "true" : "false";
            string countText = _count.Value.ToString(CultureInfo.InvariantCulture);
            string greetingText = GreetingName(_greeting.Value);
            ctx.Log.Info("hello:config enabled=" + enabledText + " count=" + countText + " greeting=" + greetingText);
            _count.OnChanged(OnCountChanged);
            _greeting.OnChanged(OnGreetingChanged);

            _hooksEnabled = ctx.Config.Bind("hooks", "enabled", false, "安装示例钩子（实机验证用）");
            _redirectGrid = ctx.Config.Bind("hooks", "redirectGrid", -1, "Move 前置把目标格改成此值（-1 不改）");
            _skipBoard = ctx.Config.Bind("hooks", "skipBoard", false, "GetHandCards 前置跳过原方法");
            _throwOnce = ctx.Config.Bind("hooks", "throwOnce", false, "Move 前置抛一次异常");
            _throwInEvent = ctx.Config.Bind("hooks", "throwInEvent", false, "为 true 时在配置回调里抛一次异常（验证普通回调的错误隔离）");
            _throwInEvent.OnChanged(OnThrowInEventChanged);
            _throwCaught = ctx.Config.Bind("hooks", "throwCaught", false, "Move 前置内部 throw 并自行 catch（诊断）");
            _throwInSelfIsPlayer = ctx.Config.Bind("hooks", "throwInSelfIsPlayer", false, "SelfIsPlayer 前置抛一次异常（由游戏自身触发，诊断）");
            _hooksEnabled.OnChanged(OnHooksEnabledChanged);
            if (_hooksEnabled.Value) InstallHooks();

            bool hotkeyOk = ctx.Input.RegisterHotkey("greet", "F8", Greet);
            string hotkeyText = hotkeyOk ? "ok" : "conflict";
            ctx.Log.Info("hello:hotkey " + hotkeyText);

            ctx.Services.Register<HelloService>(new HelloService());
            string serviceText = ctx.Services.Get<HelloService>() != null ? "ok" : "missing";
            ctx.Log.Info("hello:service " + serviceText);

            ctx.Events.OnRoundStarted(OnRound);
            // 仅示例：订阅全部消息（<object>）会拖慢游戏，生产 mod 按具体类型订阅（如 OnNetIn<Proto.GameStatus>）。
            ctx.Events.OnNetIn<object>(OnNetIn);
            ctx.MainThread.Delay(1f, OnDelay);
            _frameTask = ctx.MainThread.EveryFrame(OnFrame);
        }

        public override void OnGameReady()
        {
            Context.Log.Info("hello:game-ready");
            if (_enabled.Value) Ui.Toast("Hello 示例 mod 已加载（按 F8 问候）");
        }

        public override void OnSceneChanged(GameScene scene)
        {
            Context.Log.Info("hello:scene " + SceneName(scene));
        }

        public override void OnUpdate()
        {
            _updates++;
            if (_updates == 60) Context.Log.Info("hello:update 60");
        }

        public override void OnDisable()
        {
            // 宿主会在停用/热重载时自动清理本 mod 的订阅、热键、主线程任务、服务与钩子；这里的 Cancel 只是演示 ISubscription 用法。
            if (_frameTask != null) _frameTask.Cancel();
            Context.Log.Info("hello:disable");
        }

        void OnCountChanged(int value)
        {
            Context.Log.Info("hello:count-changed " + value.ToString(CultureInfo.InvariantCulture));
        }

        void OnGreetingChanged(Greeting value)
        {
            Context.Log.Info("hello:greeting-changed " + GreetingName(value));
        }

        void OnRound(int round)
        {
            Context.Log.Info("hello:round " + round.ToString(CultureInfo.InvariantCulture));
        }

        void OnNetIn(object message)
        {
            _netIn++;
            if (_netIn == 1) Context.Log.Info("hello:net-in first");
        }

        void OnDelay()
        {
            Context.Log.Info("hello:delay-fired");
        }

        void OnFrame()
        {
            _frames++;
            if (_frames == 30) Context.Log.Info("hello:frames 30");
        }

        void Greet()
        {
            Context.Log.Info("hello:greet");
            Ui.Toast("你好，道友！");
        }

        void OnThrowInEventChanged(bool value)
        {
            if (!value) return;
            Context.Log.Info("hello:event-throwing");
            throw new InvalidOperationException("event 测试异常");
        }

        void OnHooksEnabledChanged(bool enabled)
        {
            if (enabled) InstallHooks();
        }

        void InstallHooks()
        {
            if (_hooksInstalled) return;
            _hooksInstalled = true;
            int installed = 0;
            installed += TryPrefix("CardPanel", "GetHandCards", 0, OnGetHandCardsPrefix);
            installed += TryPostfix("CardPanel", "GetHandCards", 0, OnGetHandCardsPostfix);
            installed += TryPrefix("Yx.ModSdk.Game.PlacementApi", "Move", 2, OnMovePrefix);
            installed += TryPrefix("GameStatusExtension", "SelfIsPlayer", 1, OnSelfIsPlayerPrefix);
            installed += TryPostfix("Yx.ModSdk.Game.PlacementApi", "Board", 0, OnBoardPostfix);
            installed += TryPrefix("CardPanel", "MoveToGrid", 2, OnMoveToGridPrefix);
            Context.Log.Info("hello:hooks-installed " + installed.ToString(CultureInfo.InvariantCulture));
        }

        int TryPrefix(string type, string method, int paramCount, Func<HookContext, bool> handler)
        {
            try
            {
                Context.Hooks.Prefix(type, method, paramCount, handler);
                return 1;
            }
            catch (Exception e)
            {
                Context.Log.Warn("hello:hook-failed " + type + "." + method + ": " + e.Message);
                return 0;
            }
        }

        int TryPostfix(string type, string method, int paramCount, Action<HookContext> handler)
        {
            try
            {
                Context.Hooks.Postfix(type, method, paramCount, handler);
                return 1;
            }
            catch (Exception e)
            {
                Context.Log.Warn("hello:hook-failed " + type + "." + method + ": " + e.Message);
                return 0;
            }
        }

        bool OnGetHandCardsPrefix(HookContext h)
        {
            Context.Log.Info("hook:prefix GetHandCards");
            if (_skipBoard.Value) h.Skip(null);
            return true;
        }

        void OnGetHandCardsPostfix(HookContext h)
        {
            IList list = h.Result as IList;
            string count = list == null ? "null" : list.Count.ToString(CultureInfo.InvariantCulture);
            Context.Log.Info("hook:postfix GetHandCards count=" + count);
        }

        bool OnMovePrefix(HookContext h)
        {
            int redirect = _redirectGrid.Value;
            if (redirect >= 0) h.Args[1] = redirect;
            string grid = h.Args[1] == null ? "null" : ((int)h.Args[1]).ToString(CultureInfo.InvariantCulture);
            Context.Log.Info("hook:prefix Move grid=" + grid);
            if (_throwCaught.Value)
            {
                _throwCaught.Value = false;
                try
                {
                    throw new InvalidOperationException("caught 测试");
                }
                catch (Exception e)
                {
                    Context.Log.Info("hello:hook-caught " + e.Message);
                }
            }
            if (_throwOnce.Value)
            {
                _throwOnce.Value = false;
                throw new InvalidOperationException("hook 测试异常");
            }
            return true;
        }

        bool OnSelfIsPlayerPrefix(HookContext h)
        {
            Context.Log.Info("hook:prefix SelfIsPlayer");
            if (_throwInSelfIsPlayer.Value)
            {
                _throwInSelfIsPlayer.Value = false;
                throw new InvalidOperationException("SelfIsPlayer 测试异常");
            }
            return true;
        }

        void OnBoardPostfix(HookContext h)
        {
            Result<List<HandCard>> r = h.Result as Result<List<HandCard>>;
            string ok = r == null ? "null" : (r.Ok ? "true" : "false");
            Context.Log.Info("hook:postfix Board ok=" + ok);
        }

        bool OnMoveToGridPrefix(HookContext h)
        {
            Context.Log.Info("hook:prefix MoveToGrid");
            return true;
        }

        static string GreetingName(Greeting g)
        {
            switch (g)
            {
                case Greeting.Hello: return "Hello";
                case Greeting.Welcome: return "Welcome";
                default: return "?";
            }
        }

        static string SceneName(GameScene s)
        {
            switch (s)
            {
                case GameScene.Lobby: return "Lobby";
                case GameScene.Placement: return "Placement";
                case GameScene.Battle: return "Battle";
                case GameScene.Home: return "Home";
                default: return "Unknown";
            }
        }
    }
}
