using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using StickApp.Models;
using StickApp.Services;

namespace StickApp;

public partial class NoteWindow : Window
{
    public static bool SuppressCloseHandling = false;

    public Note Note { get; }
    public event Action? Changed;
    public event Action<NoteWindow>? Removed;

    private bool _isLoaded;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private static readonly string[] BackgroundExtensions = { ".png", ".jpg", ".jpeg" };

    private const double MinImageSize = 30;
    private const double MaxImageSize = 220;

    public NoteWindow(Note note)
    {
        InitializeComponent();
        Note = note;
        LoadNote();
        _isLoaded = true;
    }

    private void LoadNote()
    {
        Left = Note.X;
        Top = Note.Y;
        Width = Note.Width;
        Height = Note.Height;
        ApplyBackground(Note.Color);
        NoteText.Text = Note.Text;
        RefreshImages();
    }

    private void ApplyBackground(string fallbackColorHex)
    {
        var backgroundPath = BackgroundExtensions
            .Select(ext => Path.Combine(AppContext.BaseDirectory, "Assets", "background" + ext))
            .FirstOrDefault(File.Exists);

        if (backgroundPath is null)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackColorHex);
            RootGrid.Background = new SolidColorBrush(color);
            BackgroundImage.Visibility = Visibility.Collapsed;
            BackgroundImage.Source = null;
            return;
        }

        RootGrid.Background = System.Windows.Media.Brushes.Black;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(backgroundPath);
        bitmap.EndInit();

        BackgroundImage.Source = bitmap;
        BackgroundImage.Opacity = 1.0;
        BackgroundImage.Effect = new BlurEffect { Radius = 10 };
        BackgroundImage.Visibility = Visibility.Visible;
    }

    private void RefreshImages()
    {
        ImagesPanel.Children.Clear();
        foreach (var entry in Note.Images)
        {
            ImagesPanel.Children.Add(BuildThumbnail(entry));
        }

        ImagesRow.Height = Note.Images.Count > 0 ? GridLength.Auto : new GridLength(0);
        ClearPhotosItem.IsEnabled = Note.Images.Count > 0;
    }

    private FrameworkElement BuildThumbnail(NoteImage entry)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(NoteStore.GetImagePath(entry.FileName));
        bitmap.EndInit();

        var image = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            Height = entry.Size
        };

        var container = new System.Windows.Controls.Grid
        {
            Height = entry.Size,
            Margin = new Thickness(2)
        };
        container.Children.Add(image);

        var removeButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            FontSize = 9,
            Style = (Style)FindResource("HeaderIconButtonStyle"),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top
        };
        removeButton.Click += (_, _) => RemoveImage(entry.FileName);
        container.Children.Add(removeButton);

        var resizeGrip = new System.Windows.Controls.Border
        {
            Width = 16,
            Height = 16,
            Background = System.Windows.Media.Brushes.Black,
            Opacity = 0.5,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Cursor = System.Windows.Input.Cursors.SizeNWSE
        };

        double dragStartSize = 0;
        System.Windows.Point dragStartPoint = default;

        resizeGrip.MouseLeftButtonDown += (_, e) =>
        {
            dragStartSize = entry.Size;
            dragStartPoint = e.GetPosition(this);
            resizeGrip.CaptureMouse();
            e.Handled = true;
        };
        resizeGrip.MouseMove += (_, e) =>
        {
            if (!resizeGrip.IsMouseCaptured) return;
            var current = e.GetPosition(this);
            double delta = Math.Max(current.X - dragStartPoint.X, current.Y - dragStartPoint.Y);
            double newSize = Math.Clamp(dragStartSize + delta, MinImageSize, MaxImageSize);
            entry.Size = newSize;
            image.Height = newSize;
            container.Height = newSize;
        };
        resizeGrip.MouseLeftButtonUp += (_, _) =>
        {
            if (!resizeGrip.IsMouseCaptured) return;
            resizeGrip.ReleaseMouseCapture();
            RaiseChanged();
        };
        container.Children.Add(resizeGrip);

        return container;
    }

    private void RaiseChanged()
    {
        Note.ModifiedUtc = DateTime.UtcNow;
        Changed?.Invoke();
    }

    private void AddImageFromFile(string path)
    {
        Note.Images.Add(new NoteImage { FileName = NoteStore.CopyImageIn(path) });
        RefreshImages();
        RaiseChanged();
    }

    private void RemoveImage(string fileName)
    {
        NoteStore.DeleteImage(fileName);
        Note.Images.RemoveAll(i => i.FileName == fileName);
        RefreshImages();
        RaiseChanged();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragMove();
        Note.X = Left;
        Note.Y = Top;
        RaiseChanged();
    }

    private void NoteWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isLoaded) return;
        Note.Width = Width;
        Note.Height = Height;
        RaiseChanged();
    }

    private void NoteWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (!_isLoaded) return;
        Note.X = Left;
        Note.Y = Top;
        RaiseChanged();
    }

    private void NoteText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        Note.Text = NoteText.Text;
        RaiseChanged();
    }

    private void AddPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            Multiselect = true
        };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                AddImageFromFile(file);
            }
        }
    }

    private void ClearPhotos_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in Note.Images)
        {
            NoteStore.DeleteImage(entry.FileName);
        }
        Note.Images.Clear();
        RefreshImages();
        RaiseChanged();
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void NoteWindow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        DeleteButton.Visibility = Visibility.Visible;
        AddPhotoButton.Visibility = Visibility.Visible;
    }

    private void NoteWindow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        DeleteButton.Visibility = Visibility.Collapsed;
        AddPhotoButton.Visibility = Visibility.Collapsed;
    }

    private void NoteWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && System.Windows.Clipboard.ContainsImage())
        {
            var image = System.Windows.Clipboard.GetImage();
            if (image is not null)
            {
                Note.Images.Add(new NoteImage { FileName = NoteStore.SaveClipboardImage(image) });
                RefreshImages();
                RaiseChanged();
            }
            e.Handled = true;
        }
    }

    private void NoteWindow_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        var imageFiles = files.Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        foreach (var file in imageFiles)
        {
            AddImageFromFile(file);
        }
    }

    private void NoteWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (SuppressCloseHandling) return;
        foreach (var entry in Note.Images)
        {
            NoteStore.DeleteImage(entry.FileName);
        }
        Removed?.Invoke(this);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_APPWINDOW = 0x40000;

    private void NoteWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
    }
}
