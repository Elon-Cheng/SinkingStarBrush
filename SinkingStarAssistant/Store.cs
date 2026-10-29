using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SinkingStarAssistant;

public record Dot(double X, double Y);
public class Mark
{
    public string Tool { get; set; } = "画笔";
    public string Color { get; set; } = "#FFCE67";
    public double Width { get; set; } = 4;
    public int Number { get; set; }
    public List<Dot> Points { get; set; } = [];
}
public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未命名谜题";
    public string Memo { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public List<Mark> Marks { get; set; } = [];
    public override string ToString() => $"{Title}   ·   {Created.ToLocalTime():MM-dd HH:mm}";
}
public class Settings
{
    public string CaptureKey { get; set; } = "F8";
    public string ToolbarKey { get; set; } = "F9";
}
static class Store
{
    public static string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SinkingStarAssistant");
    public static string Records => Path.Combine(Root, "records");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Folder(Note n) => Path.Combine(Records, n.Id);
    public static void AtomicWrite(string path, string data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp"; File.WriteAllText(temp, data); File.Move(temp, path, true);
    }
    public static void Save(Note note, BitmapSource original, BitmapSource annotated)
    {
        var dir = Folder(note); Directory.CreateDirectory(dir);
        if (!File.Exists(Path.Combine(dir, "original.png"))) Png(original, Path.Combine(dir, "original.png"));
        Png(annotated, Path.Combine(dir, "annotated.png"));
        AtomicWrite(Path.Combine(dir, "note.json"), JsonSerializer.Serialize(note, Json));
    }
    public static void Png(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using (var file = File.Create(path + ".tmp")) encoder.Save(file);
        File.Move(path + ".tmp", path, true);
    }
    public static BitmapSource Image(string path)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit(); image.Freeze(); return image;
    }
    public static List<Note> List()
    {
        Directory.CreateDirectory(Records); var notes = new List<Note>();
        foreach (var file in Directory.EnumerateFiles(Records, "note.json", SearchOption.AllDirectories))
        {
            try
            {
                var n = JsonSerializer.Deserialize<Note>(File.ReadAllText(file));
                if (n != null && Guid.TryParseExact(n.Id, "N", out _) && File.Exists(Path.Combine(Folder(n), "original.png"))) notes.Add(n);
            }
            catch (Exception e) when (e is JsonException or IOException) { }
        }
        return notes.OrderByDescending(n => n.Created).ToList();
    }
    public static Settings LoadSettings()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public static void SaveSettings(Settings settings) => AtomicWrite(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(settings, Json));
}
