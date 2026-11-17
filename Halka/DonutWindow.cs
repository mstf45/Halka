using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Halka.Core;

namespace Halka;

public sealed class DonutWindow : Window
{
    readonly DonutControl _ring = new();
    IReadOnlyList<Slice> _front = [];
    IReadOnlyList<Slice> _back = [];
    IReadOnlyList<string> _files = [];
    string _note = "";
    bool _backFace;
    int _quality;

    public bool ClickMode { get; }
    public bool HadDrop { get; private set; }
    public event Action<Slice, IReadOnlyList<string>>? Chosen;

    public DonutWindow(bool clickMode)
    {
        ClickMode = clickMode;
        Width = 520;
        Height = 520;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = clickMode;
        Background = new SolidColorBrush(Color.FromRgb(18, 20, 26));
        Content = _ring;
        AllowDrop = true;
        _ring.SliceClicked += OnSliceClicked;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                Flip();
            }
        };
        MouseWheel += (_, e) =>
        {
            if (ClickMode && e.Delta != 0)
                Flip();
        };
    }

    public void Load(Profile profile, IReadOnlyList<string> files, int quality)
    {
        _front = profile.Front;
        _back = profile.Back;
        _files = files.ToList();
        _note = profile.Note;
        _quality = quality;
        _backFace = false;
        ShowFace();
    }

    public void Flip()
    {
        if (_backFace && _front.Count == 0)
            return;
        if (!_backFace && _back.Count == 0)
            return;
        _backFace = !_backFace;
        ShowFace();
    }

    public void PreparePixels(int x, int y)
    {
        var scale = NativeMethods.DpiAt(x, y) / 96.0;
        Left = x / scale - Width / 2;
        Top = y / scale - Height / 2;
    }

    public void PlaceAtPixels(int x, int y)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;

        var dip = source.CompositionTarget.TransformFromDevice.Transform(new Point(x, y));
        Left = dip.X - Width / 2;
        Top = dip.Y - Height / 2;
        var bounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        if (Left < bounds.Left)
            Left = bounds.Left;
        if (Top < bounds.Top)
            Top = bounds.Top;
        if (Left + Width > bounds.Right)
            Left = Math.Max(bounds.Left, bounds.Right - Width);
        if (Top + Height > bounds.Bottom)
            Top = Math.Max(bounds.Top, bounds.Bottom - Height);
    }

    public void HoverPixels(int x, int y)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return;
        var dip = source.CompositionTarget.TransformFromDevice.Transform(new Point(x, y));
        _ring.Hover(_ring.PointFromScreen(new Point(dip.X, dip.Y)));
    }

    public void ApplyRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var dipW = ActualWidth > 1 ? ActualWidth : Width;
        var dipH = ActualHeight > 1 ? ActualHeight : Height;
        var w = Math.Max(1, (int)Math.Round(dipW * dpi.DpiScaleX));
        var h = Math.Max(1, (int)Math.Round(dipH * dpi.DpiScaleY));
        NativeMethods.SetWindowRgn(hwnd, NativeMethods.CreateEllipticRgn(0, 0, w, h), true);
        if (ClickMode)
            return;

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var updated = new IntPtr(style.ToInt64() | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, updated);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyRegion();
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int mouseActivate = 0x0021;
        const int noActivate = 3;
        if (!ClickMode && msg == mouseActivate)
        {
            handled = true;
            return new IntPtr(noActivate);
        }

        return IntPtr.Zero;
    }

    protected override void OnDragEnter(DragEventArgs e) => AcceptDrag(e, load: true);

    protected override void OnDragOver(DragEventArgs e) => AcceptDrag(e, load: false);

    protected override void OnDrop(DragEventArgs e)
    {
        HadDrop = true;
        e.Handled = true;
        var files = Read(e);
        if (files.Count == 0)
            return;
        if (_files.Count == 0)
            Load(Profiles.For(files, FfmpegOps.Find(CurrentSettings()) != null), files, _quality);

        var slices = Current();
        var hit = _ring.Hit(e.GetPosition(_ring));
        if (hit < 0 || hit >= slices.Count)
            return;
        Chosen?.Invoke(slices[hit], _files);
    }

    void AcceptDrag(DragEventArgs e, bool load)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        _ring.Hover(e.GetPosition(_ring));
        if (!load)
            return;

        var files = Read(e);
        if (files.Count > 0)
            Load(Profiles.For(files, FfmpegOps.Find(CurrentSettings()) != null), files, _quality);
    }

    void OnSliceClicked(int index)
    {
        var slices = Current();
        if (index < 0 || index >= slices.Count || _files.Count == 0)
            return;
        HadDrop = true;
        Chosen?.Invoke(slices[index], _files);
    }

    void ShowFace()
    {
        var slices = Current();
        var title = _files.Count switch
        {
            0 => "Bırak",
            1 => "1 dosya",
            _ => $"{_files.Count} dosya"
        };
        var subtitle = !string.IsNullOrWhiteSpace(_note)
            ? _note
            : _back.Count == 0 ? "" : _backFace ? "Tab · ön yüz" : "Tab · arka yüz";
        _ring.Set(slices, title, subtitle, $"Kalite {_quality}");
    }

    IReadOnlyList<Slice> Current() => _backFace ? _back : _front;

    static string? CurrentSettings() =>
        Application.Current is App app ? app.Settings.FfmpegPath : null;

    static List<string> Read(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];
        return paths.Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
