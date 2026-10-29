using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinkingStarAssistant;

public class DrawingSurface : FrameworkElement
{
    public BitmapSource Original { get; }
    public List<Mark> Marks { get; private set; }
    public string Tool { get; set; } = "画笔";
    public string InkColor { get; set; } = "#FFCE67";
    public double InkWidth { get; set; } = 4;
    public bool ShowMarks { get; set; } = true;
    public bool Dirty { get; set; }
    readonly Stack<string> undo = new(), redo = new();
    Mark? current;
    bool erasing;
    public DrawingSurface(BitmapSource original, List<Mark> marks)
    {
        Original = original; Marks = marks; Width = original.PixelWidth; Height = original.PixelHeight;
        Focusable = true; Cursor = Cursors.Cross; ClipToBounds = true;
        LostMouseCapture += (_, _) => { current = null; erasing = false; };
    }
    string Snapshot() => JsonSerializer.Serialize(Marks);
    void Checkpoint() { undo.Push(Snapshot()); redo.Clear(); Dirty = true; }
    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Push(Snapshot()); Marks = JsonSerializer.Deserialize<List<Mark>>(undo.Pop())!; Dirty = true; InvalidateVisual();
    }
    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Push(Snapshot()); Marks = JsonSerializer.Deserialize<List<Mark>>(redo.Pop())!; Dirty = true; InvalidateVisual();
    }
    public void Clear() { if (Marks.Count == 0) return; Checkpoint(); Marks.Clear(); InvalidateVisual(); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!ShowMarks) return;
        Focus(); CaptureMouse(); Checkpoint(); var p = e.GetPosition(this);
        if (Tool == "橡皮") { erasing = true; Erase(p); return; }
        current = new Mark { Tool = Tool, Color = InkColor, Width = InkWidth, Number = Marks.Where(m => m.Tool == "编号").Select(m => m.Number).DefaultIfEmpty().Max() + 1, Points = [new(p.X, p.Y)] };
        Marks.Add(current); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        var raw = e.GetPosition(this); var p = new Point(Math.Clamp(raw.X, 0, Width), Math.Clamp(raw.Y, 0, Height));
        if (erasing) { Erase(p); return; }
        if (current == null || current.Tool == "编号") return;
        if (current.Tool == "画笔" || current.Points.Count == 1) current.Points.Add(new(p.X, p.Y));
        else current.Points[1] = new(p.X, p.Y);
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { current = null; erasing = false; ReleaseMouseCapture(); }
    void Erase(Point p)
    {
        for (int i = Marks.Count - 1; i >= 0; i--)
        {
            var m = Marks[i];
            if (m.Tool == "编号")
            { if ((p - Pt(m.Points[0])).Length > 20 + m.Width) continue; }
            else if (!Geometry(m).StrokeContains(new Pen(Brushes.Black, Math.Max(m.Width + 14, 20)), p)) continue;
            Marks.RemoveAt(i); InvalidateVisual(); break;
        }
    }
    static Point Pt(Dot d) => new(d.X, d.Y);
    public static Geometry Geometry(Mark mark)
    {
        if (mark.Points.Count == 0) return System.Windows.Media.Geometry.Empty;
        var a = Pt(mark.Points[0]); var b = Pt(mark.Points[^1]);
        if (mark.Tool == "矩形") return new RectangleGeometry(new Rect(a, b));
        if (mark.Tool == "圆圈") return new EllipseGeometry(new Rect(a, b));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(a, false, false);
            if (mark.Points.Count == 1) ctx.LineTo(new(a.X + 0.1, a.Y + 0.1), true, false);
            else foreach (var point in mark.Points.Skip(1)) ctx.LineTo(Pt(point), true, false);
            if (mark.Tool == "箭头" && (b - a).Length > 1)
            {
                var v = b - a; v.Normalize(); var n = new Vector(-v.Y, v.X); double size = 12 + mark.Width * 2;
                ctx.BeginFigure(b - v * size + n * size * .5, false, false); ctx.LineTo(b, true, false); ctx.LineTo(b - v * size - n * size * .5, true, false);
            }
        }
        geometry.Freeze(); return geometry;
    }
    protected override void OnRender(DrawingContext dc) => Paint(dc, ShowMarks);
    public void Paint(DrawingContext dc, bool marks)
    {
        dc.DrawImage(Original, new Rect(0, 0, Width, Height));
        if (!marks) return;
        foreach (var mark in Marks)
        {
            if (mark.Points.Count == 0) continue;
            var brush = Ui.Brush(mark.Color); var pen = new Pen(brush, mark.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (mark.Tool == "编号")
            {
                var p = Pt(mark.Points[0]); dc.DrawEllipse(Ui.Brush("#DD101827"), pen, p, 16 + mark.Width, 16 + mark.Width);
                var text = new FormattedText(mark.Number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, brush, 1);
                dc.DrawText(text, new(p.X - text.Width / 2, p.Y - text.Height / 2));
            }
            else dc.DrawGeometry(null, pen, Geometry(mark));
        }
    }
    public BitmapSource Export()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) Paint(dc, true);
        var bitmap = new RenderTargetBitmap(Original.PixelWidth, Original.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
