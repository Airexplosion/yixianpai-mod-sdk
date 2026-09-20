using System.Globalization;
using System.Text;

namespace YxRoundNote
{
    /// <summary>
    /// 统计数字本身：纯逻辑，不碰 SDK、不碰游戏——所以能在普通的单元测试里随便测，也是整个 mod 里唯一值得「认真测」的部分。
    /// 写 mod 时尽量把逻辑放进这样的类里，入口类（RoundNoteMod）只负责把 SDK 的事件接到它上面。
    /// </summary>
    public sealed class RoundStats
    {
        /// <summary>这次启动游戏以来经过的回合数。</summary>
        public int SessionRounds;
        /// <summary>这次启动游戏以来打完的斗法场数。</summary>
        public int SessionBattles;
        /// <summary>当前这场斗法里双方一共出手了多少次（由钩子统计；钩子没挂上就一直是 0）。</summary>
        public int TurnsThisBattle;
        /// <summary>历史累计的回合数（存档）。</summary>
        public int LifetimeRounds;
        /// <summary>最近一次听到的回合号；还没进过对局是 0。</summary>
        public int CurrentRound;

        public void OnRound(int round)
        {
            CurrentRound = round;
            SessionRounds++;
            LifetimeRounds++;
        }

        public void OnBattleStarted()
        {
            TurnsThisBattle = 0;
        }

        public void OnTurn()
        {
            TurnsThisBattle++;
        }

        public void OnBattleEnded()
        {
            SessionBattles++;
        }

        /// <summary>清掉本次启动的数字；历史累计保留。</summary>
        public void ResetSession()
        {
            SessionRounds = 0;
            SessionBattles = 0;
            TurnsThisBattle = 0;
        }

        /// <summary>
        /// 面板 / 日志上显示的文字。注意 ILRuntime 的写法约束：拼接只拼 string，数字一律
        /// ToString(CultureInfo.InvariantCulture)；换行用 (char)10，不写转义序列。
        /// </summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("第 ").Append(N(CurrentRound)).Append(" 回合");
            sb.Append((char)10).Append("本次启动：").Append(N(SessionRounds)).Append(" 回合 / ").Append(N(SessionBattles)).Append(" 场斗法");
            sb.Append((char)10).Append("这场斗法出手：").Append(N(TurnsThisBattle)).Append(" 次");
            sb.Append((char)10).Append("历史累计：").Append(N(LifetimeRounds)).Append(" 回合");
            return sb.ToString();
        }

        static string N(int value) { return value.ToString(CultureInfo.InvariantCulture); }
    }
}
