using System.Collections.Generic;
using System.Linq;
using Xunit;
using Yx.ModSdk.Hosting;
using Yx.ModSdk.Testing;
using Yx.Shared;
using MyMod;

public class ModMainTests
{
    const string Id = "com.example.mymod";

    // 管理器下发的配置是 section → key → value；数字按 JSON 语义用 double。
    static Dictionary<string, object> Config(int greetCount)
    {
        return new Dictionary<string, object>
        {
            ["general"] = new Dictionary<string, object> { ["greetCount"] = (double)greetCount }
        };
    }

    [Fact]
    public void LoadsAndLogsVersions()
    {
        using (TestHost host = TestHost.Start(gameVersion: "9.9"))
        {
            ModMain mod = host.Load<ModMain>(Id, "0.1.0");
            Assert.NotNull(mod);
            Assert.Equal(ModStates.Running, host.Find(Id).State);
            Assert.Contains(host.Logs(Id), l => l.Msg.Contains("已加载，SDK " + SdkRuntime.SdkVersion) && l.Msg.Contains("游戏 9.9"));
        }
    }

    [Fact]
    public void HotkeyGreetsConfiguredTimes()
    {
        using (TestHost host = TestHost.Start())
        {
            ModMain mod = host.Load<ModMain>(Id, "0.1.0", config: Config(2));
            Assert.NotNull(mod);
            Assert.Equal(1, host.PressHotkey("F9"));
            Assert.Equal(2, host.Logs(Id).Count(l => l.Msg == "你好，弈仙牌！"));
            Assert.Contains(host.Logs(Id), l => l.Msg == "打招呼 x2");
        }
    }

    [Fact]
    public void RoundEventWritesDebugLog()
    {
        using (TestHost host = TestHost.Start())
        {
            ModMain mod = host.Load<ModMain>(Id, "0.1.0");
            Assert.NotNull(mod);
            host.Publish(Topics.Round, 3);
            Assert.Contains(host.Logs(Id), l => l.Lvl == "debug" && l.Msg == "回合 3");
        }
    }
}
