using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland;

// File shelf: drop files onto the island to park them, drag them back out wherever they're needed.
// Only paths are kept; nothing is copied or moved until the user drops a file somewhere.
public partial class MainWindow
{
    const int ShelfLimit = 8;

    readonly List<string> _shelf = [];
    readonly DispatcherTimer _shelfCloseTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    bool _dragInside;     // a file drag is over the island or the drop zone
    bool _draggingOut;    // we started the drag ourselves; ignore it coming back in
    Point _tilePress;

    void InitShelf()
    {
        _shelfCloseTimer.Tick += (_, _) =>
        {
            _shelfCloseTimer.Stop();
            if (_expand == ExpandKind.Shelf && !Island.IsMouseOver && !_dragInside)
                Expand(ExpandKind.None);
        };
    }

    /// <summary>The drop zone exists only while a mouse button is held, so ordinary clicks pass through.</summary>
    void UpdateDropZone(bool leftDown)
    {
        var enabled = leftDown && !_fullscreen && Settings.Get(Feature.Shelf);
        DropZone.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    void Shelf_DragEnter(object sender, DragEventArgs e)
    {
        if (_draggingOut || !HasFiles(e) || !Settings.Get(Feature.Shelf))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        _dragInside = true;
        _shelfCloseTimer.Stop();
        _collapseTimer.Stop();
        if (_expand != ExpandKind.Shelf)
            Expand(ExpandKind.Shelf);
        ShelfDropHint.Stroke = new SolidColorBrush(BlueColor);
    }

    void Shelf_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_draggingOut && HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Shelf_DragLeave(object sender, DragEventArgs e)
    {
        _dragInside = false;
        ShelfDropHint.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "SecondaryText");
        // Moving between the drop zone and the island fires leave+enter; only close if it really left.
        _shelfCloseTimer.Stop();
        _shelfCloseTimer.Start();
    }

    void Shelf_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _dragInside = false;
        ShelfDropHint.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "SecondaryText");
        if (_draggingOut || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        foreach (var path in paths)
        {
            _shelf.Remove(path);
            _shelf.Add(path);
        }
        while (_shelf.Count > ShelfLimit)
            _shelf.RemoveAt(0);

        BuildShelf();
        _shelfCloseTimer.Stop();
        _shelfCloseTimer.Start();
    }

    void BuildShelf()
    {
        _shelf.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        ShelfItems.Children.Clear();
        ShelfEmptyText.Visibility = _shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfDropHint.Visibility = _shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfClear.Visibility = _shelf.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShelfCompactText.Text = _shelf.Count == 1 ? "Rafta 1 dosya" : $"Rafta {_shelf.Count} dosya";

        foreach (var path in _shelf)
            ShelfItems.Children.Add(CreateTile(path));
    }

    FrameworkElement CreateTile(string path)
    {
        var name = new TextBlock
        {
            Text = Path.GetFileName(path.TrimEnd('\\')),
            FontSize = 10.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Fg");

        var tile = new StackPanel
        {
            Width = 76,
            Margin = new Thickness(0, 0, 8, 8),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = path,
        };
        tile.Children.Add(new Image { Source = FileIcon(path), Width = 36, Height = 36 });
        tile.Children.Add(name);

        tile.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (e.ClickCount == 2)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { /* no app for this file */ }
                return;
            }
            _tilePress = e.GetPosition(this);
        };
        tile.MouseLeftButtonUp += (_, e) => e.Handled = true;
        tile.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - _tilePress).Length < 5)
                return;
            DragOut(tile, path);
        };
        return tile;
    }

    void DragOut(FrameworkElement tile, string path)
    {
        _draggingOut = true;
        try
        {
            DragDrop.DoDragDrop(tile, new DataObject(DataFormats.FileDrop, new[] { path }),
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            _draggingOut = false;
        }

        // Moved somewhere else: it no longer belongs on the shelf.
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _shelf.Remove(path);
            BuildShelf();
        }
    }

    void ShelfClear_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _shelf.Clear();
        BuildShelf();
        Expand(ExpandKind.None);
    }

    static ImageSource? FileIcon(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var icon = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            icon.Freeze();
            return icon;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    const uint SHGFI_ICON = 0x100;
    const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}
