#if YX_GAME
using Yx.ModSdk;
using Yx.ModSdk.Unity;
using UnityEngine;
using UnityEngine.UI;
namespace ClassicPredictionExample
{
    public sealed class Demo : YxMod
    {
        NativePanelTheme _theme;
        NativePredictionWindow _window;
        NativePredictionRow _row;
        int _rank;
        public override void OnLoad(ModContext ctx)
        {
            _theme = new NativePanelTheme(ctx);
            ctx.Input.RegisterHotkey("classicUi", "Ctrl+Shift+F8", Toggle);
        }
        void Toggle()
        {
            if (_window != null) { _window.Root.SetActive(!_window.Root.activeSelf); return; }
            Canvas canvas=Ui.FindRootCanvas(); if(canvas==null)return;
            _theme.SetSource(canvas.transform);
            _window=new NativePredictionWindow(Context,_theme,"旧版预测面板示例");
            if(_window.Root==null){_window=null;return;}
            _row=new NativePredictionRow(Context,_theme,_window.Table.transform,0,Minus,Plus,Submit,Half,Sell);
            _row.PlayerName.text="示例玩家";_row.PlayerDetail.text="命元 80 · 2胜0负";
            _row.Badge.gameObject.SetActive(false);_row.BadgeText.text="由MOD提供马牌";
            _row.Rank.text="—";_row.Rate.text="2.95x";_row.Holding.text="未持仓";
            _row.Half.SetActive(false);_row.Sell.SetActive(false);
            _window.Status.text="只展示UI，不修改积分";
        }
        void Minus(){_rank=_rank<=1?8:_rank-1;ShowRank();}
        void Plus(){_rank=_rank>=8?1:_rank+1;ShowRank();}
        void ShowRank(){_row.Rank.text="第"+_rank.ToString(System.Globalization.CultureInfo.InvariantCulture)+"名";}
        void Submit()
        {
            long amount;
            if(!long.TryParse(_row.Stake.Text,out amount)||amount<1||amount>10000){_window.Footer.text="投入须为1至10000整数";return;}
            _window.Footer.text="UI示例收到输入："+amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        void Half(){}
        void Sell(){}
        public override void OnDisable(){if(_window!=null)_window.Destroy();}
    }
}
#endif
