using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SinkingStarAssistant;

public class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { Environment.Exit(SelfTest.Run()); return; }
        var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Directory.CreateDirectory(Store.Root);
            File.AppendAllText(Path.Combine(Store.Root, "error.log"), $"{DateTimeOffset.Now}\n{e.Exception}\n");
            MessageBox.Show("操作未完成：" + e.Exception.Message, "沉星助手"); e.Handled = true;
        };
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(3)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush("#24324A")));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Ui.Brush("#455572")));
        app.Resources.Add(typeof(Button), style);
        app.Run(new MainWindow());
    }
}

static class Ui
{
    public static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    public static Button Button(string label, Action action)
    {
        var b = new Button { Content = label }; b.Click += (_, _) => action(); return b;
    }
    public static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, Foreground = Brushes.White, Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap };
    public static void Theme(Window w) { w.Background = Brush("#101827"); w.Foreground = Brushes.White; w.FontFamily = new FontFamily("Microsoft YaHei UI"); w.FontSize = 14; }
}
