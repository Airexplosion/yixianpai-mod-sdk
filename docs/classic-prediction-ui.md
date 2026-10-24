# 旧版预测面板组件（Classic UI）

复用旧版个人赌马界面的完整结构：窗框、54高标题栏、深色内衬、七列表头、关注高亮、玩家名/命元、马牌图文、名次调节、赔率、投入、加注/退半/清仓、持仓与结果、分隔线、底栏。组件只负责显示，调用者提供数据和回调；可用于单人或联机。

## 提供内容

- `NativePanelTheme`：从调用者提供的已初始化原生UI子树读贴图、字体、按钮/输入框模板；不打开面板、不扫描昵称、不调用游戏协议。
- `NativePredictionWindow`：旧版完整窗体、`Banner`、`Body`、`Table`、`Title/Status/Footer`，支持`SetPeek`、`SetMinimized`和主线程`UpdateDrag`。
- `NativePredictionRow`：旧版每行的尺寸和列坐标，提供`PlayerName/PlayerDetail/Badge/BadgeText/Rank/Rate/Stake/Buy/Half/Sell/Holding/Result/Highlight`等控件句柄。
- `NativeControlStyle`：复制原生控件外观，保留目标回调与输入状态。

`Badge.sprite`由MOD提供，不包含田忌图片或游戏素材。赔率仅展示调用者设置的文字，SDK不计算赔率、不读余额、不扣积分、不做网络结算。

## 引用方式

本次以附加程序集 `sdk/net40/Yx.ModSdk.ClassicUi.dll` 发布，原来的Core/ModSdk DLL未替换，旧管理器无需更新。将附加DLL与MOD放在同一plugins目录，并在manifest声明：

```json
"libs": ["plugins/Yx.ModSdk.ClassicUi.dll"]
```

mod工程同时引用Core、ModSdk、ClassicUi，主线程中创建窗口和行。完整示例在`examples/classic-prediction`，Ctrl+Shift+F8开关示例；输入只显示在底栏，不扣积分。需要自己的本机refs，设置`YX_REFS`或`-p:YxRefs=<绝对目录/>`后构建。

源码在`source/Unity/`；可用`source/ClassicUi/Yx.ModSdk.ClassicUi.csproj`重建附加DLL。若运行时已把这些类编入新的Yx.ModSdk，不要同时加载附加DLL或重复源码编译同名类型。预测MOD暂用同源兼容副本，确保旧运行时可用。

## 生命周期与限制

所有调用仅Unity主线程。`theme.SetSource(existingRoot)`只给已初始化UI子树；缓存对象失效或场景变化时提供新子树。不要每帧调用输入框SetText，编辑期间用UiInput.Show或保持当前输入，提交时读取Stake.Text。业务层自行验证金额、目标UID、房间/对局和报价版本。

`window.Destroy()`销毁窗口及其所有子控件，图标Sprite/Texture由调用者自己管理。透视只放行主体点击，顶栏继续可用。拖拽只在标题区域开始，避开按钮；位置持久化由MOD自行保存。

模板不完整时使用保底色和游戏字体；这不等于完整原生外观已经验收。源码、附加DLL及示例net40构建0warning/0error，IL静态检查0error/0warning；游戏中字体材质、比例与最终视觉尚待实机验收。未打包Unity/游戏DLL、游戏反编译源码、马图或用户数据。

## 旧版角色下注条

`NativePredictionBar`保留旧版角色下注条的暗色内衬、马牌图文、名次加减、独立倍率、“投/分”输入、下注/退半/清仓、持仓、总览和折叠。去掉用户明确不要的走势入口，其他控件沿用旧坐标，条宽990、高66（收起按钮加宽为76）。深色Backing是独立不透明节点，不会被原生白纸Window贴图覆盖；收起时隐藏实际子节点和点击区域。

构造参数依次为ctx、theme、minus、plus、buy、half、sell、overview、collapse回调；通过Root/Badge/BadgeText/PlayerName/Rank/Rate/Stake/Buy/Half/Sell/Holding等句柄更新数据。输入初始化10，但刷新时不得重复SetText；目标房间/玩家变化时可由业务层重置。SetCollapsed仅控制视觉，不提交操作。Destroy销毁下注条，图标Sprite仍由调用者管理。示例已包含窗体及下注条。

收起按钮使用“收起 / 展开”文字，不再使用游戏字体可能缺失的下拉符号。此变更不改变回调和折叠子控件行为。
