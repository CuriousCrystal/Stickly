using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using StickApp.Models;
using StickApp.Services;

using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfPoint = System.Windows.Point;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WpfImage = System.Windows.Controls.Image;
using WpfButton = System.Windows.Controls.Button;
using WpfGrid = System.Windows.Controls.Grid;
using WpfBorder = System.Windows.Controls.Border;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace StickApp;

public partial class NoteWindow : Window
{
    public static bool SuppressCloseHandling = false;

    public Note Note { get; }
    public event Action? Changed;
    public event Action<NoteWindow>? Removed;
    public event Action? NewNoteRequested;

    private bool _isLoaded;
    private bool _isExplicitlyDeleted;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    private const double MinImageSize = 30;
    private const double MaxImageSize = 220;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public NoteWindow(Note note)
    {
        InitializeComponent();
        Note = note;
        LoadNote();
        _isLoaded = true;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Topmost = true;
        Topmost = false;
        Activate();
        Focus();
        NoteText.Focus();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    private void LoadNote()
    {
        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;
        double screenWidth = SystemParameters.VirtualScreenWidth;
        double screenHeight = SystemParameters.VirtualScreenHeight;

        double targetWidth = Math.Max(MinWidth, Math.Min(Note.Width, screenWidth));
        double targetHeight = Math.Max(MinHeight, Math.Min(Note.Height, screenHeight));

        double targetLeft = Note.X;
        double targetTop = Note.Y;

        if (targetLeft + targetWidth < screenLeft + 40 || targetLeft > screenLeft + screenWidth - 40)
        {
            targetLeft = screenLeft + 80;
        }

        if (targetTop + 30 < screenTop || targetTop > screenTop + screenHeight - 60)
        {
            targetTop = screenTop + 80;
        }

        Left = targetLeft;
        Top = targetTop;
        Width = targetWidth;
        Height = targetHeight;
        Note.X = Left;
        Note.Y = Top;
        Note.Width = Width;
        Note.Height = Height;

        ApplyNoteColor(Note.Color);
        NoteText.Text = Note.Text;
        RefreshImages();
    }

    private static readonly string[] BackgroundExtensions = { ".png", ".jpg", ".jpeg" };

    private void ApplyNoteColor(string fallbackColorHex)
    {
        var backgroundPath = BackgroundExtensions
            .Select(ext => Path.Combine(AppContext.BaseDirectory, "Assets", "background" + ext))
            .FirstOrDefault(File.Exists);

        bool useImage = (fallbackColorHex == "Image" || (backgroundPath is not null && (string.IsNullOrEmpty(fallbackColorHex) || fallbackColorHex == "#FFF6A6")));

        if (useImage && backgroundPath is not null)
        {
            RootGrid.Background = WpfBrushes.Black;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(backgroundPath);
            bitmap.EndInit();
            bitmap.Freeze();

            BackgroundImage.Source = bitmap;
            BackgroundImage.Opacity = 1.0;
            BackgroundImage.Effect = new BlurEffect { Radius = 10 };
            BackgroundImage.Visibility = Visibility.Visible;

            NoteText.Foreground = WpfBrushes.Yellow;
            NoteText.CaretBrush = WpfBrushes.Yellow;
            NewNoteButton.Foreground = WpfBrushes.White;
            AddPhotoButton.Foreground = WpfBrushes.White;
            DeleteButton.Foreground = WpfBrushes.White;
            return;
        }

        BackgroundImage.Visibility = Visibility.Collapsed;
        BackgroundImage.Source = null;

        WpfColor color;
        try
        {
            color = (WpfColor)WpfColorConverter.ConvertFromString(fallbackColorHex);
        }
        catch
        {
            color = (WpfColor)WpfColorConverter.ConvertFromString("#FFF6A6");
        }

        RootGrid.Background = new SolidColorBrush(color);

        // Compute perceived luminance (ITU-R BT.601) to adapt text and button contrast
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
        bool isDark = luminance < 135;

        var textColor = isDark ? WpfBrushes.White : new SolidColorBrush(WpfColor.FromRgb(30, 30, 30));
        NoteText.Foreground = textColor;
        NoteText.CaretBrush = textColor;
        NewNoteButton.Foreground = textColor;
        AddPhotoButton.Foreground = textColor;
        DeleteButton.Foreground = textColor;
    }

    private void RefreshImages()
    {
        ImagesPanel.Children.Clear();
        foreach (var entry in Note.Images)
        {
            var thumb = BuildThumbnail(entry);
            if (thumb is not null)
            {
                ImagesPanel.Children.Add(thumb);
            }
        }

        ImagesRow.Height = ImagesPanel.Children.Count > 0 ? GridLength.Auto : new GridLength(0);
        ClearPhotosItem.IsEnabled = ImagesPanel.Children.Count > 0;
    }

    private FrameworkElement? BuildThumbnail(NoteImage entry)
    {
        var imagePath = NoteStore.GetImagePath(entry.FileName);
        if (!File.Exists(imagePath)) return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.DecodePixelHeight = Math.Max(40, (int)entry.Size * 2);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new WpfImage
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Height = entry.Size
            };

            var container = new WpfGrid
            {
                Height = entry.Size,
                Margin = new Thickness(2)
            };
            container.Children.Add(image);

            var removeButton = new WpfButton
            {
                Content = "✕",
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                FontSize = 9,
                Style = (Style)FindResource("HeaderIconButtonStyle"),
                Foreground = WpfBrushes.White,
                Background = new SolidColorBrush(WpfColor.FromArgb(160, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                VerticalAlignment = WpfVerticalAlignment.Top
            };
            removeButton.Click += (_, _) => RemoveImage(entry.FileName);
            container.Children.Add(removeButton);

            var resizeGrip = new WpfBorder
            {
                Width = 14,
                Height = 14,
                Background = WpfBrushes.Black,
                Opacity = 0.4,
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                VerticalAlignment = WpfVerticalAlignment.Bottom,
                Cursor = WpfCursors.SizeNWSE
            };

            double dragStartSize = 0;
            WpfPoint dragStartPoint = default;

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

            resizeGrip.LostMouseCapture += (_, _) =>
            {
                RaiseChanged();
            };

            container.Children.Add(resizeGrip);
            return container;
        }
        catch
        {
            return null;
        }
    }

    private void RaiseChanged()
    {
        Note.ModifiedUtc = DateTime.UtcNow;
        Changed?.Invoke();
    }

    private void AddImageFromFile(string path)
    {
        try
        {
            var savedName = NoteStore.CopyImageIn(path);
            Note.Images.Add(new NoteImage { FileName = savedName });
            RefreshImages();
            RaiseChanged();
        }
        catch { }
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

    private void NoteText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded) return;
        Note.Text = NoteText.Text;
        RaiseChanged();
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        NewNoteRequested?.Invoke();
    }

    private void SetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string hexColor })
        {
            Note.Color = hexColor;
            ApplyNoteColor(hexColor);
            RaiseChanged();
        }
    }

    private void AddPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
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
        var result = WpfMessageBox.Show(
            this,
            "Are you sure you want to permanently delete this note?",
            "Delete Note",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _isExplicitlyDeleted = true;
            Close();
        }
    }

    private void NoteWindow_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        NewNoteButton.Visibility = Visibility.Visible;
        AddPhotoButton.Visibility = Visibility.Visible;
        DeleteButton.Visibility = Visibility.Visible;
    }

    private void NoteWindow_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        NewNoteButton.Visibility = Visibility.Collapsed;
        AddPhotoButton.Visibility = Visibility.Collapsed;
        DeleteButton.Visibility = Visibility.Collapsed;
    }

    private void NoteWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (WpfClipboard.ContainsText())
            {
                return;
            }

            if (WpfClipboard.ContainsImage())
            {
                try
                {
                    var image = WpfClipboard.GetImage();
                    if (image is not null)
                    {
                        var name = NoteStore.SaveClipboardImage(image);
                        Note.Images.Add(new NoteImage { FileName = name });
                        RefreshImages();
                        RaiseChanged();
                        e.Handled = true;
                    }
                }
                catch { }
            }
        }
    }

    private void NoteWindow_Drop(object sender, WpfDragEventArgs e)
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

        if (_isExplicitlyDeleted)
        {
            foreach (var entry in Note.Images)
            {
                NoteStore.DeleteImage(entry.FileName);
            }
            Removed?.Invoke(this);
        }
    }
}
