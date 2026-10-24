# Classic UI 示例

创建旧版完整预测窗体与一行演示，不读取游戏对局、不扣积分。引用SDK主DLL以及附加ClassicUi DLL；使用本机游戏refs编译。运行时Ctrl+Shift+F8开关，示例按钮只把输入回显到底栏。原生模板从已有Canvas控件读取，不激活游戏面板。马牌图片由调用者提供，本示例只显示文字。

构建：`dotnet build examples/classic-prediction -c Release -p:YxRefs=<本机refs绝对目录/>`。manifest声明libs，构建输出会带上附加DLL。请在自己的测试配置档安装，示例非游戏实机验收证明。
