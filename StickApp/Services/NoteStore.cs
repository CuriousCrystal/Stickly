using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using StickApp.Models;

namespace StickApp.Services;

public static class NoteStore
{
    private static readonly object FileLock = new();

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickApp");
    private static readonly string ImagesDir = Path.Combine(RootDir, "images");
    private static readonly string NotesFile = Path.Combine(RootDir, "notes.json");
    private static readonly string NotesBackupFile = Path.Combine(RootDir, "notes.json.bak");

    public static bool NotesFileExists => File.Exists(NotesFile);

    public static List<Note> Load()
    {
        lock (FileLock)
        {
            Directory.CreateDirectory(ImagesDir);
            if (!File.Exists(NotesFile))
            {
                if (File.Exists(NotesBackupFile))
                {
                    var backupNotes = TryDeserialize(NotesBackupFile);
                    if (backupNotes is not null) return backupNotes;
                }
                return new();
            }

            var notes = TryDeserialize(NotesFile);
            if (notes is not null)
            {
                return notes;
            }

            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var corruptPath = Path.Combine(RootDir, $"notes.json.corrupted_{timestamp}");
                File.Copy(NotesFile, corruptPath, overwrite: true);
            }
            catch { }

            if (File.Exists(NotesBackupFile))
            {
                var backupNotes = TryDeserialize(NotesBackupFile);
                if (backupNotes is not null)
                {
                    return backupNotes;
                }
            }

            return new();
        }
    }

    private static List<Note>? TryDeserialize(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json)) return new();
            return JsonSerializer.Deserialize<List<Note>>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(IEnumerable<Note> notes)
    {
        lock (FileLock)
        {
            Directory.CreateDirectory(RootDir);
            var noteList = notes.ToList();
            var json = JsonSerializer.Serialize(noteList, new JsonSerializerOptions { WriteIndented = true });
            var tmp = NotesFile + ".tmp";

            File.WriteAllText(tmp, json);

            if (File.Exists(NotesFile))
            {
                try
                {
                    File.Copy(NotesFile, NotesBackupFile, overwrite: true);
                }
                catch { }
            }

            File.Move(tmp, NotesFile, overwrite: true);
        }
    }

    public static string CopyImageIn(string sourcePath)
    {
        lock (FileLock)
        {
            Directory.CreateDirectory(ImagesDir);
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            var cleanExt = string.IsNullOrWhiteSpace(ext) ? ".png" : ext;
            var name = $"{Guid.NewGuid()}{cleanExt}";
            var targetPath = Path.Combine(ImagesDir, name);
            File.Copy(sourcePath, targetPath, overwrite: true);
            return name;
        }
    }

    public static string SaveClipboardImage(BitmapSource bitmap)
    {
        lock (FileLock)
        {
            Directory.CreateDirectory(ImagesDir);
            var name = $"{Guid.NewGuid()}.png";
            var targetPath = Path.Combine(ImagesDir, name);
            using (var fs = File.Create(targetPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fs);
            }
            return name;
        }
    }

    public static string GetImagePath(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        return Path.Combine(ImagesDir, safeFileName);
    }

    public static void DeleteImage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;

        lock (FileLock)
        {
            var path = GetImagePath(fileName);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch { }
            }
        }
    }

    public static void CleanupOrphanedImages(IEnumerable<Note> activeNotes)
    {
        lock (FileLock)
        {
            if (!Directory.Exists(ImagesDir)) return;

            try
            {
                var activeFiles = new HashSet<string>(
                    activeNotes.SelectMany(n => n.Images).Select(i => Path.GetFileName(i.FileName)),
                    StringComparer.OrdinalIgnoreCase
                );

                var dirInfo = new DirectoryInfo(ImagesDir);
                foreach (var file in dirInfo.GetFiles())
                {
                    if (!activeFiles.Contains(file.Name))
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
