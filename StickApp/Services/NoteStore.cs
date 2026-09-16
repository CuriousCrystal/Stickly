using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using StickApp.Models;

namespace StickApp.Services;

public static class NoteStore
{
    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickApp");
    private static readonly string ImagesDir = Path.Combine(RootDir, "images");
    private static readonly string NotesFile = Path.Combine(RootDir, "notes.json");

    public static bool NotesFileExists => File.Exists(NotesFile);

    public static List<Note> Load()
    {
        Directory.CreateDirectory(ImagesDir);
        if (!File.Exists(NotesFile)) return new();
        try
        {
            var json = File.ReadAllText(NotesFile);
            return JsonSerializer.Deserialize<List<Note>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void Save(IEnumerable<Note> notes)
    {
        Directory.CreateDirectory(RootDir);
        var json = JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true });
        var tmp = NotesFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, NotesFile, overwrite: true);
    }

    public static string CopyImageIn(string sourcePath)
    {
        Directory.CreateDirectory(ImagesDir);
        var name = Guid.NewGuid() + Path.GetExtension(sourcePath);
        File.Copy(sourcePath, Path.Combine(ImagesDir, name), overwrite: true);
        return name;
    }

    public static string SaveClipboardImage(BitmapSource bitmap)
    {
        Directory.CreateDirectory(ImagesDir);
        var name = Guid.NewGuid() + ".png";
        using var fs = File.Create(Path.Combine(ImagesDir, name));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(fs);
        return name;
    }

    public static string GetImagePath(string fileName) => Path.Combine(ImagesDir, fileName);

    public static void DeleteImage(string? fileName)
    {
        if (fileName is null) return;
        var path = GetImagePath(fileName);
        if (File.Exists(path)) File.Delete(path);
    }
}
