using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SinkingStarAssistant;

static class SelfTest
{
    public static int Run()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "self-test.txt");
        try
        {
            // Isolated directory intentionally retained for inspection; never touches player records.
            Store.Root = Path.Combine(Path.GetTempPath(), "SinkingStarAssistant-tests", Guid.NewGuid().ToString("N"));
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.Navy, null, new Rect(0, 0, 320, 200));
            var image = new RenderTargetBitmap(320, 200, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var note = new Note { Title = "测试关卡", Memo = "可编辑标注往返测试", Marks = [new Mark { Tool = "箭头", Points = [new(20, 30), new(200, 140)] }, new Mark { Tool = "编号", Number = 1, Points = [new(100, 100)] }] };
            var surface = new DrawingSurface(image, note.Marks);
            Store.Save(note, image, surface.Export());
            var restored = Store.List().Single();
            Require(restored.Marks.Count == 2 && restored.Marks[0].Points[1].X == 200 && restored.Memo == note.Memo, "Metadata round trip");
            var png = Store.Image(Path.Combine(Store.Folder(restored), "annotated.png"));
            Require(png.PixelWidth == 320 && png.PixelHeight == 200, "Export size");
            var originalBytes = File.ReadAllBytes(Path.Combine(Store.Folder(note), "original.png"));
            Require(!originalBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(Store.Folder(note), "annotated.png"))), "Annotations rendered");
            surface.Clear(); Require(surface.Marks.Count == 0, "Clear"); surface.Undo(); Require(surface.Marks.Count == 2, "Undo"); surface.Redo(); Require(surface.Marks.Count == 0, "Redo");
            surface.Undo(); Store.Save(note, image, surface.Export());
            Require(originalBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(Store.Folder(note), "original.png"))), "Original unchanged");
            Store.SaveSettings(new Settings { CaptureKey = "F7", ToolbarKey = "F10" }); Require(Store.LoadSettings().CaptureKey == "F7", "Settings round trip");
            Directory.CreateDirectory(Path.Combine(Store.Records, "broken")); File.WriteAllText(Path.Combine(Store.Records, "broken", "note.json"), "invalid");
            Require(Store.List().Count == 1, "Corrupt record isolation");
            File.WriteAllText(output, "PASS: metadata, PNG dimensions, rendered marks, undo/redo, original preservation, settings, corrupt-record isolation.\nArtifacts: " + Store.Root);
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(output, "FAIL: " + ex); return 1; }
    }
    static void Require(bool value, string name) { if (!value) throw new Exception(name); }
}
