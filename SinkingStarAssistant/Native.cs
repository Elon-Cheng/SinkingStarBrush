using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SinkingStarAssistant;

record WindowTarget(nint Handle, string Title) { public override string ToString() => Title; }
static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint op);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);

    public static List<WindowTarget> Windows()
    {
        var result = new List<WindowTarget>();
        EnumWindows((h, _) => {
            GetWindowThreadProcessId(h, out var pid);
            var title = new StringBuilder(512); GetWindowText(h, title, title.Capacity);
            if (pid != Environment.ProcessId && IsWindowVisible(h) && title.Length > 0)
                result.Add(new(h, title.ToString()));
            return true;
        }, 0);
        return result.OrderByDescending(x => x.Title.Contains("沉星") || x.Title.Contains("Sinking", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static RECT Bounds(nint hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || !GetClientRect(hwnd, out var r))
            throw new InvalidOperationException("目标窗口已关闭或最小化，请重新选择并显示游戏窗口。");
        var p = new POINT(); ClientToScreen(hwnd, ref p);
        r.Right += p.X; r.Bottom += p.Y; r.Left = p.X; r.Top = p.Y;
        if (r.Right <= r.Left || r.Bottom <= r.Top) throw new InvalidOperationException("目标窗口没有可捕获的画面。");
        return r;
    }

    public static BitmapSource Capture(nint hwnd)
    {
        var r = Bounds(hwnd); int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var screen = GetDC(0); var dc = CreateCompatibleDC(screen); var bitmap = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(dc, bitmap);
        try
        {
            if (bitmap == 0 || !BitBlt(dc, 0, 0, w, h, screen, r.Left, r.Top, 0x40CC0020))
                throw new InvalidOperationException("截图失败，请使用无边框或窗口模式。");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        finally { SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen); }
    }
}
