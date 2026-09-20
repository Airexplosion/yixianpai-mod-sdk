using System.Collections.Generic;
using System.Linq;
using Xunit;
using Yx.ModSdk.Hosting;
using Yx.ModSdk.Testing;
using Yx.Shared;
using YxRoundNote;

// 两类测试：
//   · RoundStats 是纯逻辑，直接 new 出来测；
//   · RoundNoteMod 用 TestHost 驱动——它用和游戏里的 Loader 同一套生命周期加载 mod，
//     配置、存档、热键、事件、钩子分发都在内存里模拟，不需要游戏。
public class RoundStatsTests
{
    [Fact]
    public void Rounds_count_for_the_session_and_for_the_lifetime()
    {
        var stats = new RoundStats { LifetimeRounds = 100 };
        stats.OnRound(1);
        stats.OnRound(2);
        Assert.Equal(2, stats.CurrentRound);
        Assert.Equal(2, stats.SessionRounds);
        Assert.Equal(102, stats.LifetimeRounds);
    }

    [Fact]
    public void Turns_restart_with_every_battle()
    {
        var stats = new RoundStats();
        stats.OnBattleStarted();
        stats.OnTurn();
        stats.OnTurn();
        stats.OnBattleEnded();
        Assert.Equal(2, stats.TurnsThisBattle);
        stats.OnBattleStarted();
        Assert.Equal(0, stats.TurnsThisBattle);
        Assert.Equal(1, stats.SessionBattles);
    }

    [Fact]
    public void Resetting_the_session_keeps_the_lifetime_total()
    {
        var stats = new RoundStats { LifetimeRounds = 7 };
        stats.OnRound(1);
        stats.ResetSession();
        Assert.Equal(0, stats.SessionRounds);
        Assert.Equal(8, stats.LifetimeRounds);
        Assert.Contains("历史累计：8 回合", stats.Describe());
    }
}

public class RoundNoteModTests
{
    const string Id = "com.yx.roundnote";

    // 管理器下发的配置是 section → key → value。
    static Dictionary<string, object> Config(bool announce, bool countTurns)
    {
        return new Dictionary<string, object>
        {
            ["general"] = new Dictionary<string, object> { ["announce"] = announce, ["countTurns"] = countTurns }
        };
    }

    [Fact]
    public void Loads_with_the_saved_lifetime_total()
    {
        using (TestHost host = TestHost.Start())
        {
            // JSON 下发的数字是 double，ctx.Data.Get<int> 会转换。
            RoundNoteMod mod = host.Load<RoundNoteMod>(Id, "1.0.0", data: new Dictionary<string, object> { ["lifetimeRounds"] = 40.0 });
            Assert.NotNull(mod);
            Assert.Equal(ModStates.Running, host.Find(Id).State);
            Assert.Equal(40, mod.Stats.LifetimeRounds);
            Assert.Contains(host.Logs(Id), l => l.Msg.Contains("历史累计 40 回合"));
        }
    }

    [Fact]
    public void A_round_is_counted_announced_and_saved()
    {
        using (TestHost host = TestHost.Start())
        {
            RoundNoteMod mod = host.Load<RoundNoteMod>(Id, "1.0.0");
            host.Publish(Topics.Round, 3);           // 游戏里由 SDK 在回合开始时发；载荷是回合号
            Assert.Equal(3, mod.Stats.CurrentRound);
            Assert.Equal(1, mod.Context.Data.Get<int>("lifetimeRounds", 0));
            Assert.Contains(host.Logs(Id), l => l.Msg == "第 3 回合（历史累计 1）");
        }
    }

    [Fact]
    public void Announcing_can_be_switched_off_in_the_config()
    {
        using (TestHost host = TestHost.Start())
        {
            host.Load<RoundNoteMod>(Id, "1.0.0", config: Config(false, true));
            host.Publish(Topics.Round, 1);
            Assert.DoesNotContain(host.Logs(Id), l => l.Msg.StartsWith("第 1 回合"));
        }
    }

    [Fact]
    public void The_hotkey_writes_the_summary_to_the_log()
    {
        using (TestHost host = TestHost.Start())
        {
            host.Load<RoundNoteMod>(Id, "1.0.0");
            host.Publish(Topics.Round, 5);
            Assert.Equal(1, host.PressHotkey("F7"));      // 返回触发了几个回调
            Assert.Contains(host.Logs(Id), l => l.Msg.Contains("第 5 回合") && l.Msg.Contains("本次启动：1 回合"));
        }
    }

    [Fact]
    public void Turns_are_counted_through_the_game_hook()
    {
        using (TestHost host = TestHost.Start())
        {
            RoundNoteMod mod = host.Load<RoundNoteMod>(Id, "1.0.0");
            host.Publish(Topics.BattleStarted, null);
            object result;
            // 模拟游戏调了两次被钩住的方法：前置返回 false = 没有跳过原方法。
            Assert.False(host.InvokePrefix("BattleCharacter", "OnTurnStarted", 0, null, new object[0], out result));
            host.InvokePrefix("BattleCharacter", "OnTurnStarted", 0, null, new object[0], out result);
            host.Publish(Topics.BattleEnded, null);
            Assert.Equal(2, mod.Stats.TurnsThisBattle);
            Assert.Contains(host.Logs(Id), l => l.Msg == "斗法结束：这场双方共出手 2 次");
        }
    }

    [Fact]
    public void A_hook_that_cannot_be_installed_costs_one_number_not_the_whole_mod()
    {
        using (TestHost host = TestHost.Start())
        {
            // 模拟游戏更新后这个方法没了：mod 照常加载，只是记一条 warn。
            host.FailHookInstall("BattleCharacter", "OnTurnStarted", "找不到方法 BattleCharacter.OnTurnStarted");
            RoundNoteMod mod = host.Load<RoundNoteMod>(Id, "1.0.0");
            Assert.NotNull(mod);
            Assert.Equal(ModStates.Running, host.Find(Id).State);
            Assert.Contains(host.Logs(Id), l => l.Lvl == "warn" && l.Msg.Contains("出手次数统计不可用"));
            host.Publish(Topics.Round, 1);
            Assert.Equal(1, mod.Stats.SessionRounds);
        }
    }

    [Fact]
    public void Switching_turn_counting_off_removes_the_hook_handler()
    {
        using (TestHost host = TestHost.Start())
        {
            host.Load<RoundNoteMod>(Id, "1.0.0");
            HookSite site = host.Hooks.Sites.Single();
            Assert.Equal(1, site.HandlerCount);
            host.SetConfig(Id, Config(true, false));          // 相当于用户在管理器里关掉了这个开关
            Assert.Equal(0, site.HandlerCount);
        }
    }
}
