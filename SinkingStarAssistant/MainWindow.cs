using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SinkingStarAssistant;

class MainWindow : Window
{
    readonly ComboBox targets = new() { MinWidth = 300, Margin = new Thickness(4), DisplayMemberPath = "Title" };
    readonly ListBox records = new() { Margin = new Thickness(4), Background = Ui.Brush("#172236"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
    readonly Image preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    readonly TextBlock status = Ui.Text("就绪", 12);
    readonly Settings settings = Store.LoadSettings();
    readonly ComboBox captureKey = new() { Width = 80, Margin = new Thickness(4) }, toolbarKey = new() { Width = 80, Margin = new Thickness(4) };
    ToolbarWindow? toolbar;
    EditorWindow? editor;
    HwndSource? source;
    nint handle;
    bool capturing;
    public MainWindow()
    {
        Title = "沉星助手 · 解谜工作台"; Width = 1080; Height = 740; MinWidth = 820; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.Theme(this);
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(Ui.Text("✦  沉星助手", 30));
        var subtitle = Ui.Text("画下思路，留下关键一帧。    PREVIEW  0.1", 13); subtitle.Foreground = Ui.Brush("#A9B9D0"); top.Children.Add(subtitle);
        var bar = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        bar.Children.Add(targets); bar.Children.Add(Ui.Button("刷新窗口", RefreshTargets)); bar.Children.Add(Ui.Button("截帧并标注", () => _ = Capture()));
        bar.Children.Add(Ui.Button("悬浮工具栏", ToggleToolbar)); bar.Children.Add(Ui.Button("导入图片", Import)); top.Children.Add(bar);
        top.Children.Add(Ui.Text("选择游戏窗口后截帧。请保持窗口完整可见，使用窗口化或无边框模式。截图不会暂停游戏。", 12));
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var keys = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        string[] allowed = ["F6", "F7", "F8", "F9", "F10", "F11"];
        captureKey.ItemsSource = allowed; toolbarKey.ItemsSource = allowed;
        captureKey.SelectedItem = allowed.Contains(settings.CaptureKey) ? settings.CaptureKey : "F8";
        toolbarKey.SelectedItem = allowed.Contains(settings.ToolbarKey) ? settings.ToolbarKey : "F9";
        keys.Children.Add(Ui.Text("截帧快捷键")); keys.Children.Add(captureKey); keys.Children.Add(Ui.Text("工具栏快捷键")); keys.Children.Add(toolbarKey);
        keys.Children.Add(Ui.Button("应用热键", ApplyKeys)); keys.Children.Add(Ui.Button("打开记录目录", () => { Directory.CreateDirectory(Store.Records); Process.Start(new ProcessStartInfo(Store.Records) { UseShellExecute = true }); }));
        bottom.Children.Add(keys); bottom.Children.Add(status);
        var center = new Grid { Margin = new Thickness(0, 12, 0, 0) }; center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); center.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(center);
        var left = new DockPanel(); center.Children.Add(left);
        var historyTitle = Ui.Text("解谜记录", 19); DockPanel.SetDock(historyTitle, Dock.Top); left.Children.Add(historyTitle);
        var recordActions = new WrapPanel(); recordActions.Children.Add(Ui.Button("继续编辑", EditSelected)); recordActions.Children.Add(Ui.Button("刷新记录", RefreshRecords));
        recordActions.Children.Add(Ui.Button("前后对照", Compare)); DockPanel.SetDock(recordActions, Dock.Bottom); left.Children.Add(recordActions); left.Children.Add(records);
        var right = new DockPanel { Background = Ui.Brush("#172236"), Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(right, 1); center.Children.Add(right);
        var previewLabel = Ui.Text("画面预览 · 双击记录可继续编辑", 13); DockPanel.SetDock(previewLabel, Dock.Top); right.Children.Add(previewLabel); right.Children.Add(preview);
        records.SelectionChanged += (_, _) =>
        {
            if (records.SelectedItem is Note n)
            {
                var file = Path.Combine(Store.Folder(n), "annotated.png"); preview.Source = Store.Image(File.Exists(file) ? file : Path.Combine(Store.Folder(n), "original.png"));
            }
            else preview.Source = null;
        };
        records.MouseDoubleClick += (_, _) => EditSelected();
        SourceInitialized += (_, _) => { handle = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(handle); source.AddHook(Hook); ApplyKeys(); };
        Loaded += (_, _) => { RefreshTargets(); RefreshRecords(); };
        Closing += (_, e) => { if (editor != null) { editor.Close(); if (editor != null) e.Cancel = true; } };
        Closed += (_, _) => { Native.UnregisterHotKey(handle, 1); Native.UnregisterHotKey(handle, 2); source?.RemoveHook(Hook); toolbar?.Close(); };
    }
    void RefreshTargets()
    {
        var selected = (targets.SelectedItem as WindowTarget)?.Handle;
        var windows = Native.Windows(); targets.ItemsSource = windows;
        targets.SelectedItem = windows.FirstOrDefault(w => w.Handle == selected) ?? windows.FirstOrDefault(w => w.Title.Contains("沉星") || w.Title.Contains("Sinking", StringComparison.OrdinalIgnoreCase));
        status.Text = windows.Count == 0 ? "未找到窗口。请先启动游戏，也可导入图片试用画笔。" : "已刷新窗口列表。请确认选择的是游戏窗口。";
    }
    void RefreshRecords()
    {
        string? id = (records.SelectedItem as Note)?.Id; var notes = Store.List(); records.ItemsSource = notes;
        records.SelectedItem = notes.FirstOrDefault(n => n.Id == id) ?? notes.FirstOrDefault();
        if (notes.Count == 0) status.Text = "还没有记录。截帧或导入图片，保存第一份解谜方案。";
    }
    void ApplyKeys()
    {
        var a = (string)captureKey.SelectedItem; var b = (string)toolbarKey.SelectedItem;
        if (a == b) { status.Text = "两个快捷键不能相同，原快捷键保持不变。"; return; }
        Native.UnregisterHotKey(handle, 1); Native.UnregisterHotKey(handle, 2);
        bool one = Register(1, a), two = Register(2, b);
        if (!one || !two)
        {
            Native.UnregisterHotKey(handle, 1); Native.UnregisterHotKey(handle, 2);
            bool restoredA = Register(1, settings.CaptureKey), restoredB = Register(2, settings.ToolbarKey);
            status.Text = $"快捷键被占用：{(!one ? a : "")} {(!two ? b : "")}。请更换。原热键恢复：截帧{(restoredA ? "成功" : "失败")} / 工具栏{(restoredB ? "成功" : "失败")}。"; return;
        }
        settings.CaptureKey = a; settings.ToolbarKey = b; Store.SaveSettings(settings);
        status.Text = $"热键已启用：{a} 截帧  /  {b} 工具栏";
    }
    bool Register(int id, string key) => Enum.TryParse<Key>(key, out var parsed) && Native.RegisterHotKey(handle, id, 0x4000, (uint)KeyInterop.VirtualKeyFromKey(parsed));
    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0312) { handled = true; if (wParam == 1) _ = Capture(); if (wParam == 2) ToggleToolbar(); } return 0;
    }
    public void ToggleToolbar()
    {
        if (toolbar == null) { toolbar = new ToolbarWindow(() => _ = Capture(), () => { Show(); WindowState = WindowState.Normal; Activate(); }); toolbar.Closed += (_, _) => toolbar = null; toolbar.Show(); }
        else if (toolbar.IsVisible) toolbar.Hide(); else toolbar.Show();
    }
    async Task Capture()
    {
        if (capturing) return;
        if (editor != null) { editor.Activate(); return; }
        if (targets.SelectedItem is not WindowTarget target) { status.Text = "请先选择游戏窗口。"; Show(); return; }
        capturing = true; bool mainVisible = IsVisible, toolbarVisible = toolbar?.IsVisible == true;
        try
        {
            Native.Bounds(target.Handle); Hide(); toolbar?.Hide();
            Native.SetForegroundWindow(target.Handle); await Task.Delay(250);
            if (Native.GetForegroundWindow() != target.Handle) throw new InvalidOperationException("无法切到目标窗口。请先点击游戏，再按截帧快捷键。");
            var bitmap = Native.Capture(target.Handle);
            OpenEditor(bitmap, new Note { Title = "解谜方案 " + DateTime.Now.ToString("MM-dd HH:mm:ss") }, target.Handle, mainVisible, toolbarVisible);
        }
        catch (Exception ex)
        {
            if (mainVisible) Show(); if (toolbarVisible) toolbar?.Show();
            status.Text = ex.Message; MessageBox.Show(ex.Message, "截帧未完成");
        }
        finally { capturing = false; }
    }
    void OpenEditor(System.Windows.Media.Imaging.BitmapSource bitmap, Note note, nint returnWindow = 0, bool restoreMain = true, bool restoreToolbar = false)
    {
        editor = new EditorWindow(bitmap, note); editor.Saved += RefreshRecords;
        editor.Closed += (_, _) =>
        {
            editor = null; if (restoreMain) Show(); if (restoreToolbar) toolbar?.Show();
            if (returnWindow != 0) Native.SetForegroundWindow(returnWindow);
        };
        editor.Show(); editor.Activate();
    }
    void EditSelected()
    {
        if (editor != null) { editor.Activate(); return; }
        if (records.SelectedItem is Note n) OpenEditor(Store.Image(Path.Combine(Store.Folder(n), "original.png")), System.Text.Json.JsonSerializer.Deserialize<Note>(System.Text.Json.JsonSerializer.Serialize(n))!);
    }
    void Import()
    {
        if (editor != null) { editor.Activate(); return; }
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog(this) == true) OpenEditor(Store.Image(dialog.FileName), new Note { Title = Path.GetFileNameWithoutExtension(dialog.FileName) });
    }
    void Compare()
    {
        if (records.SelectedItem is not Note n) return;
        var all = Store.List(); var other = all.Where(x => x.Id != n.Id).ToList();
        var window = new Window { Title = "沉星助手 · 前后对照", Width = 1200, Height = 700, Owner = this }; Ui.Theme(window);
        var root = new DockPanel { Margin = new Thickness(15) }; window.Content = root;
        var selector = new ComboBox { ItemsSource = other, Margin = new Thickness(5), MinHeight = 32 }; DockPanel.SetDock(selector, Dock.Top); root.Children.Add(selector);
        var label = Ui.Text("左侧：当前记录。右侧：选择另一条记录；未选择时显示当前原图。", 13); DockPanel.SetDock(label, Dock.Top); root.Children.Add(label);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(grid);
        var left = new Image { Source = Store.Image(Path.Combine(Store.Folder(n), "annotated.png")), Margin = new Thickness(5) };
        var right = new Image { Source = Store.Image(Path.Combine(Store.Folder(n), "original.png")), Margin = new Thickness(5) }; Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right);
        selector.SelectionChanged += (_, _) => { if (selector.SelectedItem is Note selected) right.Source = Store.Image(Path.Combine(Store.Folder(selected), "annotated.png")); }; window.Show();
    }
}

class ToolbarWindow : Window
{
    public ToolbarWindow(Action capture, Action home)
    {
        Title = "沉星助手 · 浮条"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Left = SystemParameters.WorkArea.Right - 380; Top = SystemParameters.WorkArea.Top + 35; Ui.Theme(this);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) }; Content = bar;
        var grip = Ui.Text("⠿  沉星", 16); grip.Cursor = Cursors.SizeAll; grip.MouseLeftButtonDown += (_, _) => DragMove(); bar.Children.Add(grip);
        bar.Children.Add(Ui.Button("截帧 / 画笔", capture)); bar.Children.Add(Ui.Button("记录", home)); bar.Children.Add(Ui.Button("收起", Hide));
    }
}
