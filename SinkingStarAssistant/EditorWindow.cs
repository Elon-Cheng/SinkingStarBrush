using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace SinkingStarAssistant;

class EditorWindow : Window
{
    readonly DrawingSurface drawing;
    readonly Note note;
    readonly TextBox title, memo;
    readonly TextBlock status = Ui.Text("画笔就绪 · 左键绘制 · Ctrl+Z 撤销 · Ctrl+Y 重做", 12);
    readonly Viewbox view;
    readonly ScrollViewer scroll;
    public event Action? Saved;
    bool textDirty;
    public EditorWindow(BitmapSource original, Note note)
    {
        this.note = note; Title = "沉星助手 · 定格分析"; Width = 1240; Height = 850; MinWidth = 850; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.Theme(this);
        drawing = new DrawingSurface(original, note.Marks);
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var header = new DockPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Ui.Text("✦  定格分析", 23));
        var exit = Ui.Button("返回游戏  Esc", Close); DockPanel.SetDock(exit, Dock.Right); header.Children.Insert(0, exit);
        var hint = Ui.Text("静态截图 · 游戏可能仍在运行    /    标注自动按原始画面坐标保存", 12); hint.Foreground = Ui.Brush("#A9B9D0");
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var tools = new WrapPanel { Margin = new Thickness(0, 7, 0, 7) }; DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        var select = new ComboBox { Width = 90, Margin = new Thickness(4), ItemsSource = new[] { "画笔", "直线", "箭头", "矩形", "圆圈", "编号", "橡皮" }, SelectedIndex = 0 };
        select.SelectionChanged += (_, _) => drawing.Tool = (string)select.SelectedItem; tools.Children.Add(select);
        foreach (var color in new[] { "#FFCE67", "#FF647C", "#5DE2CC", "#7BABFF", "#FFFFFF" })
        {
            var b = Ui.Button("●", () => { drawing.InkColor = color; status.Text = "已切换画笔颜色"; }); b.Foreground = Ui.Brush(color); b.ToolTip = color; tools.Children.Add(b);
        }
        var width = new ComboBox { Width = 65, Margin = new Thickness(4), ItemsSource = new double[] { 2, 4, 7, 12 }, SelectedIndex = 1 };
        width.SelectionChanged += (_, _) => drawing.InkWidth = (double)width.SelectedItem; tools.Children.Add(width);
        tools.Children.Add(Ui.Button("撤销", drawing.Undo)); tools.Children.Add(Ui.Button("重做", drawing.Redo));
        tools.Children.Add(Ui.Button("原图 / 标注", () => { drawing.ShowMarks = !drawing.ShowMarks; drawing.InvalidateVisual(); }));
        tools.Children.Add(Ui.Button("清空", () => { if (MessageBox.Show(this, "清空所有标注？清空后仍可撤销。", "清空标注", MessageBoxButton.YesNo) == MessageBoxResult.Yes) drawing.Clear(); }));
        tools.Children.Add(Ui.Button("适应", () => Fit()));
        tools.Children.Add(Ui.Button("放大 +", () => Zoom(1.25))); tools.Children.Add(Ui.Button("缩小 −", () => Zoom(.8)));

        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); fields.ColumnDefinitions.Add(new ColumnDefinition());
        title = new TextBox { Text = note.Title, Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = "关卡 / 方案名称" };
        memo = new TextBox { Text = note.Memo, Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = "解谜备注", MaxLength = 20000 };
        title.TextChanged += (_, _) => textDirty = true; memo.TextChanged += (_, _) => textDirty = true;
        fields.Children.Add(title); Grid.SetColumn(memo, 1); fields.Children.Add(memo); bottom.Children.Add(fields);
        var actions = new WrapPanel(); actions.Children.Add(Ui.Button("保存记录  Ctrl+S", Save));
        actions.Children.Add(Ui.Button("另存为新方案", SaveCopy));
        actions.Children.Add(Ui.Button("导出标注 PNG", () => Export(false))); actions.Children.Add(Ui.Button("导出原图", () => Export(true)));
        actions.Children.Add(status); bottom.Children.Add(actions);
        view = new Viewbox { Child = drawing, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        scroll = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Black };
        root.Children.Add(scroll); Loaded += (_, _) => Fit();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.S) { Save(); e.Handled = true; }
            if (Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Z) { drawing.Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { drawing.Redo(); e.Handled = true; }
        };
        Closing += (_, e) =>
        {
            if (!drawing.Dirty && !textDirty && File.Exists(Path.Combine(Store.Folder(note), "note.json"))) return;
            var answer = MessageBox.Show(this, "保存这次截图和标注？", "返回游戏", MessageBoxButton.YesNoCancel);
            if (answer == MessageBoxResult.Cancel) e.Cancel = true;
            else if (answer == MessageBoxResult.Yes) { try { Save(); } catch { e.Cancel = true; throw; } }
        };
    }
    void Fit()
    {
        var scale = Math.Min(Math.Max(1, scroll.ActualWidth - 25) / drawing.Width, Math.Max(1, scroll.ActualHeight - 25) / drawing.Height);
        view.Width = drawing.Width * scale; view.Height = drawing.Height * scale;
    }
    void Zoom(double ratio) { double scale = Math.Clamp(view.Width / drawing.Width * ratio, .1, 4); view.Width = drawing.Width * scale; view.Height = drawing.Height * scale; }
    void UpdateNote() { note.Title = string.IsNullOrWhiteSpace(title.Text) ? "未命名谜题" : title.Text.Trim(); note.Memo = memo.Text; note.Marks = drawing.Marks; }
    void Save()
    {
        UpdateNote(); Store.Save(note, drawing.Original, drawing.Export()); drawing.Dirty = false; textDirty = false;
        status.Text = "已保存 · " + DateTime.Now.ToString("HH:mm:ss"); Saved?.Invoke();
    }
    void SaveCopy()
    {
        var previousId = note.Id; var previousCreated = note.Created; var previousTitle = title.Text;
        note.Id = Guid.NewGuid().ToString("N"); note.Created = DateTimeOffset.Now; title.Text += " · 新方案";
        try { Save(); }
        catch { note.Id = previousId; note.Created = previousCreated; title.Text = previousTitle; throw; }
    }
    void Export(bool original)
    {
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = original ? "沉星原图.png" : "沉星标注.png" };
        if (dialog.ShowDialog(this) == true) { Store.Png(original ? drawing.Original : drawing.Export(), dialog.FileName); status.Text = "图片已导出"; }
    }
}
