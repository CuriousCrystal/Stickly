using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using StickApp.Models;
using StickApp.Services;
using Forms = System.Windows.Forms;

namespace StickApp;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    private readonly List<NoteWindow> _windows = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private Forms.NotifyIcon? _tray;
    private int _newNoteOffset;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "StickApp-SingleInstance-Mutex", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            FlushSave();
        };

        bool isFirstRun = !NoteStore.NotesFileExists;
        var notes = NoteStore.Load();
        if (notes.Count == 0)
        {
            notes.Add(new Note
            {
                X = 200,
                Y = 200,
                Text = "Welcome to Stick App!\n\nType here. Right-click for photo/color options. Drag the top strip to move, drag an edge to resize."
            });
        }

        foreach (var note in notes)
        {
            CreateNoteWindow(note);
        }

        SetupTray();

        if (isFirstRun)
        {
            AutostartService.SetEnabled(true);
        }
    }

    private void CreateNoteWindow(Note note)
    {
        var window = new NoteWindow(note);
        window.Changed += RequestSave;
        window.Removed += NoteWindow_Removed;
        _windows.Add(window);
        window.Show();
    }

    private void NoteWindow_Removed(NoteWindow window)
    {
        window.Changed -= RequestSave;
        window.Removed -= NoteWindow_Removed;
        _windows.Remove(window);
        RequestSave();
    }

    private void CreateNote()
    {
        _newNoteOffset = (_newNoteOffset + 24) % 200;
        var note = new Note { X = 240 + _newNoteOffset, Y = 180 + _newNoteOffset };
        CreateNoteWindow(note);
        RequestSave();
    }

    private void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        NoteStore.Save(_windows.Select(w => w.Note));
    }

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New Note", null, (_, _) => CreateNote());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var autostartItem = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        menu.Opening += (_, _) => autostartItem.Checked = AutostartService.IsEnabled();
        autostartItem.Click += (_, _) => AutostartService.SetEnabled(autostartItem.Checked);
        menu.Items.Add(autostartItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Stick App",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => CreateNote();
    }

    private static Icon LoadTrayIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(icoPath))
        {
            return new Icon(icoPath);
        }

        var candidates = new[] { "icon.png", "icon.jpg", "icon.jpeg" };
        foreach (var name in candidates)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (File.Exists(path))
            {
                using var bitmap = new Bitmap(path);
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
        return Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!;
    }

    private void ExitApp()
    {
        NoteWindow.SuppressCloseHandling = true;
        _saveTimer.Stop();
        FlushSave();
        if (_tray is not null) _tray.Visible = false;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NoteWindow.SuppressCloseHandling = true;
        FlushSave();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
