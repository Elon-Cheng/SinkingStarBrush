using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PainterSetup;
class App : Application
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { Environment.Exit(SetupTests.Run()); return; }
        new App().Run(new SetupWindow());
    }
}
class SetupWindow : Window
{
    readonly TextBox path = new() { Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 8) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), MinHeight = 60 };
    readonly TextBlock running = new() { Margin = new Thickness(0, 8, 0, 8) };
    readonly Button install, remove;
    readonly TextBlock updateStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    readonly Button checkUpdate, downloadUpdate, baiduUpdate;
    UpdateRelease? latest;
    bool updateBusy;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    public SetupWindow()
    {
        Title = "沉星画笔 · 安装与管理"; Width = 730; Height = 690; MinWidth = 640; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(15, 46, 57)); Foreground = new SolidColorBrush(Color.FromRgb(255, 218, 153)); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 15;
        var root = new StackPanel { Margin = new Thickness(30) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "沉 星 画 笔", FontSize = 32, FontFamily = new FontFamily("SimSun"), Margin = new Thickness(0, 0, 0, 14) });
        root.Children.Add(new TextBlock { Text = $"Windows 游戏内插件  ·  v{InstallService.Version}" });
        root.Children.Add(path);
        var paths = new WrapPanel(); paths.Children.Add(Button("自动查找游戏", Find)); paths.Children.Add(Button("选择游戏目录", Browse)); root.Children.Add(paths);
        root.Children.Add(running); root.Children.Add(status);
        var actions = new WrapPanel();
        install = Button("安装 / 更新 / 修复", () => Run(() => { InstallService.Install(path.Text); status.Text = "安装成功。请从 Steam 启动游戏，新版本将在启动时加载。"; })); actions.Children.Add(install);
        remove = Button("卸载插件", () => Run(() => { if (MessageBox.Show(this, "卸载画笔插件？保存的笔迹和设置将保留。", "卸载插件", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; InstallService.Uninstall(path.Text); status.Text = "已卸载插件，笔迹和设置仍保留。"; })); actions.Children.Add(remove);
        actions.Children.Add(Button("启动游戏", () => Run(() => { InstallService.Validate(path.Text); Process.Start(new ProcessStartInfo("steam://rungameid/499170") { UseShellExecute = true }); })));
        root.Children.Add(actions);
        var links = new WrapPanel(); links.Children.Add(Button("刷新状态", Refresh)); links.Children.Add(Button("打开日志", () => Run(() => { var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SinkingStarAssistant", "native"); Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }))); root.Children.Add(links);
        links.Children.Add(Button("开源许可", () => { using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ImGui.License")!; using var reader = new StreamReader(stream); MessageBox.Show(this, reader.ReadToEnd(), "Dear ImGui · MIT License"); }));
        var updates = new WrapPanel();
        checkUpdate = Button("检查更新", () => _ = CheckUpdates()); updates.Children.Add(checkUpdate);
        downloadUpdate = Button("下载并校验安装器", () => _ = DownloadUpdate()); downloadUpdate.IsEnabled = false; updates.Children.Add(downloadUpdate);
        baiduUpdate = Button("百度网盘下载", () => Run(() => { if (latest != null && UpdateService.ValidBaidu(latest.Manifest.BaiduUrl)) Process.Start(new ProcessStartInfo(latest.Manifest.BaiduUrl) { UseShellExecute = true }); })); baiduUpdate.IsEnabled = false; updates.Children.Add(baiduUpdate);
        root.Children.Add(updates); root.Children.Add(updateStatus);
        updateStatus.Text = UpdateService.ValidRepository(UpdateService.Config.Repository) ? "点击检查 GitHub 最新正式版。网络不可用时，仍可离线安装。" : "尚未配置发布仓库，当前可离线安装。";
        root.Children.Add(new TextBlock { Text = "仅支持 Windows x64 Steam 版。安装前请退出游戏。\n游戏内 F8 画笔，齿轮打开设置；开关自动保存。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 16, 0, 0), FontSize = 12 });
        Loaded += (_, _) => { Find(); timer.Start(); }; Closed += (_, _) => timer.Stop();
        timer.Tick += (_, _) => UpdateRunning();
    }
    Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 8), Background = new SolidColorBrush(Color.FromRgb(34, 66, 74)), Foreground = Foreground, BorderBrush = new SolidColorBrush(Color.FromRgb(150, 125, 80)) };
        button.Click += (_, _) => action(); return button;
    }
    void Run(Action action) { try { action(); } catch (Exception e) { status.Text = e is UnauthorizedAccessException ? "没有写入游戏目录的权限。请右键本安装器，选择“以管理员身份运行”。" : e.Message; } UpdateRunning(); }
    void UpdateRunning() { bool active = InstallService.Running(); running.Text = active ? "游戏正在运行 · 磁盘版本不代表已加载版本，请退出后更新" : "游戏未运行 · 安装后下次启动加载新版本"; install.IsEnabled = remove.IsEnabled = !active; }
    async Task CheckUpdates()
    {
        if (updateBusy) return;
        updateBusy = true; checkUpdate.IsEnabled = downloadUpdate.IsEnabled = baiduUpdate.IsEnabled = false; latest = null;
        updateStatus.Text = "正在查询最新正式版…";
        try
        {
            latest = await UpdateService.CheckAsync();
            var comparison = UpdateService.ParseVersion(latest.Manifest.Version).CompareTo(UpdateService.ParseVersion(InstallService.Version));
            updateStatus.Text = $"最新正式版 v{latest.Manifest.Version} · 当前安装器 v{InstallService.Version}\n" + (comparison > 0 ? "发现新版本，请下载新安装器更新。" : comparison == 0 ? "安装器已是最新正式版；游戏安装状态请查看上方。" : "当前安装器高于正式版，不建议降级。") + (string.IsNullOrWhiteSpace(latest.Manifest.BaiduCode) ? "" : "\n网盘提取码：" + latest.Manifest.BaiduCode);
            downloadUpdate.IsEnabled = comparison >= 0;
            baiduUpdate.IsEnabled = comparison >= 0 && UpdateService.ValidBaidu(latest.Manifest.BaiduUrl);
        }
        catch (Exception e) { updateStatus.Text = "检查更新失败：" + e.Message + "\n当前版本仍可使用。"; }
        finally { updateBusy = false; checkUpdate.IsEnabled = true; }
    }
    async Task DownloadUpdate()
    {
        if (updateBusy || latest == null) return;
        updateBusy = true; checkUpdate.IsEnabled = downloadUpdate.IsEnabled = false;
        updateStatus.Text = "正在下载并校验安装器，请稍候…";
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SinkingStarAssistant", "downloads", latest.Manifest.Version + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, latest.Manifest.FileName);
            await UpdateService.DownloadAsync(latest, file);
            updateStatus.Text = "SHA256 校验通过。请退出游戏，运行已下载的安装器。\n" + file;
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception e) { updateStatus.Text = "下载失败：" + e.Message + "\n未安装或运行下载文件，可重试。"; }
        finally { updateBusy = false; checkUpdate.IsEnabled = downloadUpdate.IsEnabled = true; }
    }
    void Refresh() => Run(() => status.Text = InstallService.Status(path.Text));
    void Find() => Run(() => { var folders = InstallService.Discover(); if (folders.Count == 0) { status.Text = "未找到《沉星之序》。请点击“选择游戏目录”。"; return; } path.Text = folders[0]; status.Text = InstallService.Status(path.Text); });
    void Browse() { var dialog = new OpenFolderDialog { Title = "选择包含 sinking_star.exe 的文件夹" }; if (dialog.ShowDialog(this) == true) { path.Text = dialog.FolderName; Refresh(); } }
}
