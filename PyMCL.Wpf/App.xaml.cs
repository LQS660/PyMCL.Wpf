using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using PyMCL.Services;

namespace PyMCL;

public partial class App : Application
{
    public static bool IsDark { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--nav-dump"))
        {
            // 侧栏模型的跨端对照入口：tests/test_nav_parity.py 拿它跟 Qt / TS 的结果逐条比
            Environment.ExitCode = NavDump.Run();
            Shutdown(Environment.ExitCode);
            return;
        }
        if (e.Args.Contains("--ime-check"))
        {
            // 输入法回车判定的真值表自检。自动化里敲不出真键盘，让逻辑自己受考。
            Environment.ExitCode = ImeGuard.SelfTest();
            Shutdown(Environment.ExitCode);
            return;
        }
        if (e.Args.Contains("--i18n-check"))
        {
            // 多语言自检：源码里还有没有没包 L() 的中文、L() 的 key 词表里缺不缺
            Environment.ExitCode = I18nCheck.Run(e.Args);
            Shutdown(Environment.ExitCode);
            return;
        }
        if (e.Args.Contains("--consent-check"))
        {
            // 反馈同意提示的状态机真值表：首次问、选过不再问。tests/test_wpf_feedback.py 拿它当依据
            Environment.ExitCode = Pages.FeedbackConsent.SelfTest();
            Shutdown(Environment.ExitCode);
            return;
        }
        // 语言只在启动时定一次（--lang / PYMCL_LANG / config.json 的 language），切语言照 Qt 提示重启。
        // 放在探针分支之后：--nav-dump 要跟 Qt 的中文标签逐条比，不能被 config 里的语言带偏。
        I18n.Init(e.Args);
        // 动画帧率：WPF 默认 60，桌面高刷屏下拉满更跟手。
        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline), new FrameworkPropertyMetadata(60));
        RenderOptions.ProcessRenderMode = RenderMode.Default;
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);
        if (e.Args.Contains("--smoke"))
        {
            // 运行期冒烟：自带看门狗，跑完自己退，不会留窗口等人点
            Smoke.Run(e.Args);
            return;
        }
        new MainWindow().Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        Smoke.Note("unhandled", e.Exception.ToString());
        e.Handled = true;
        try
        {
            var w = Current?.MainWindow as MainWindow;
            w?.Toast(L("出错了"), e.Exception.Message, ToastKind.Error);
        }
        catch { }
    }

    /// <summary>
    /// 崩溃日志。装在 Program Files 下非管理员运行、或从只读介质启动时，
    /// AppContext.BaseDirectory 写不进去；此前整体 try/catch{} 一吞，崩溃信息就完全
    /// 没了（用户只看到一个 Toast，开发者拿不到线索）。这里依次退回 LOCALAPPDATA、TEMP。
    /// </summary>
    private static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
        foreach (var dir in new[]
                 {
                     AppContext.BaseDirectory,
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     System.IO.Path.GetTempPath(),
                 })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "pymcl-wpf-error.log"), text);
                return;
            }
            catch { }
        }
    }

    /// <summary>整字典热替换调色板，避免逐个改画刷造成的闪烁。</summary>
    public static void ApplyTheme(bool dark)
    {
        IsDark = dark;
        var dicts = Current.Resources.MergedDictionaries;
        var uri = new Uri(dark ? "Themes/Palette.Dark.xaml" : "Themes/Palette.Light.xaml", UriKind.Relative);
        var next = new ResourceDictionary { Source = uri };
        if (dicts.Count > 0) dicts[0] = next;
        else dicts.Add(next);
        // 壁纸的遮罩色与让位用的半透明底都跟着主题走，整字典一换就得重刷一遍
        Wallpaper.OnThemeChanged();
        // 整字典替换会把 SettingsPage.ApplyVisuals 写进 dicts[0] 的 9 个键
        // （B.Accent / B.AccentDeep / B.AccentLite / B.AccentSoft / B.AccentSoft2 / B.Ok /
        //  B.BannerFill / B.Canvas / B.PageWash）一起冲掉——用户自定义的主题色与背景
        // 于是被静默丢弃，切一次深浅色就退回默认。换完字典立刻按当前设置重刷一遍。
        Pages.SettingsPage.ApplyVisuals();
    }
}
