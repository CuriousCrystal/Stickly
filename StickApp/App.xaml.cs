using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using StickApp.Models;
using StickApp.Services;
using Forms = System.Windows.Forms;

namespace StickApp;

public partial class App : System.Windows.Application
{
    private const string MutexName = "StickApp-SingleInstance-Mutex";
    private const string WakeupEventName = "StickApp-Wakeup-Event";
    private const int ASFW_ANY = -1;

    private static EventWaitHandle? _wakeupEvent;
    private static RegisteredWaitHandle? _registeredWait;

    private readonly List<NoteWindow> _windows = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Forms.NotifyIcon? _tray;
    private int _newNoteOffset;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    protected override void OnStartup(StartupEventArgs e)
    {
        var current = System.Diagnostics.Process.GetCurrentProcess();
        var existingProcess = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
                                                       .FirstOrDefault(p => p.Id != current.Id);

        if (existingProcess is not null)
        {
            try
            {
                // Grant foreground permission to the running instance
                AllowSetForegroundWindow(existingProcess.Id);

                using var existingEvent = EventWaitHandle.OpenExisting(WakeupEventName);
                existingEvent.Set();
            }
            catch
            {
                // Event might not exist or be accessible
            }

            Shutdown();
            return;
        }

        try
        {
            _wakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeupEventName);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(_wakeupEvent, OnWakeupSignaled, null, -1, false);
        }
        catch
        {
            // Fallback gracefully
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickApp");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.UtcNow}] {args.Exception}\n\n");
            }
            catch { }
        };

        base.OnStartup(e);

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            FlushSave();
        };

        var notes = NoteStore.Load();
        if (notes.Count == 0)
        {
            notes.Add(new Note
            {
                X = 220,
                Y = 180,
                Text = "Welcome to Stick App!\n\n• Type your notes here.\n• Right-click for Color & Photo options.\n• Drag the top strip to move, drag an edge to resize.\n• Use '＋' on the top bar to create a new note."
            });
        }

        foreach (var note in notes)
        {
            CreateNoteWindow(note);
        }

        SetupTray();
    }

    private void OnWakeupSignaled(object? state, bool timedOut)
    {
        Dispatcher.Invoke(() =>
        {
            if (_windows.Count == 0)
            {
                CreateNote();
            }
            else
            {
                ShowAllNotes();
            }
        });
    }

    public void CreateNoteWindow(Note note)
    {
        var window = new NoteWindow(note);
        window.Changed += RequestSave;
        window.Removed += NoteWindow_Removed;
        window.NewNoteRequested += CreateNote;
        _windows.Add(window);
        window.Show();
        window.BringToFront();
    }

    private void NoteWindow_Removed(NoteWindow window)
    {
        window.Changed -= RequestSave;
        window.Removed -= NoteWindow_Removed;
        window.NewNoteRequested -= CreateNote;
        _windows.Remove(window);
        RequestSave();
    }

    public void CreateNote()
    {
        _newNoteOffset = (_newNoteOffset + 28) % 220;
        var note = new Note
        {
            X = 240 + _newNoteOffset,
            Y = 180 + _newNoteOffset,
            Color = "#FFF6A6"
        };
        CreateNoteWindow(note);
        RequestSave();
    }

    public void ShowAllNotes()
    {
        foreach (var window in _windows)
        {
            window.BringToFront();
        }
    }

    private void RequestSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        var activeNotes = _windows.Select(w => w.Note).ToList();
        NoteStore.Save(activeNotes);
        NoteStore.CleanupOrphanedImages(activeNotes);
    }

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New Note", null, (_, _) => CreateNote());
        menu.Items.Add("Show All Notes", null, (_, _) => ShowAllNotes());
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
        _tray.DoubleClick += (_, _) => ShowAllNotes();
    }

    private static Icon LoadTrayIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(icoPath))
        {
            try
            {
                return new Icon(icoPath);
            }
            catch { }
        }

        var candidates = new[] { "icon.png", "icon.jpg", "icon.jpeg" };
        foreach (var name in candidates)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (File.Exists(path))
            {
                try
                {
                    using var bitmap = new Bitmap(path);
                    IntPtr hIcon = bitmap.GetHicon();
                    var icon = (Icon)Icon.FromHandle(hIcon).Clone();
                    DestroyIcon(hIcon);
                    return icon;
                }
                catch { }
            }
        }

        return Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? SystemIcons.Application;
    }

    private void ExitApp()
    {
        NoteWindow.SuppressCloseHandling = true;
        _saveTimer.Stop();
        FlushSave();
        if (_tray is not null) _tray.Visible = false;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        NoteWindow.SuppressCloseHandling = true;
        _saveTimer.Stop();
        FlushSave();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        NoteWindow.SuppressCloseHandling = true;
        FlushSave();

        _registeredWait?.Unregister(null);
        _wakeupEvent?.Dispose();
        _tray?.Dispose();

        base.OnExit(e);
    }
}
