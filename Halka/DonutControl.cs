using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Halka.Core;

namespace Halka;

sealed class DonutControl : FrameworkElement
{
    IReadOnlyList<Slice> _slices = [];
    int _hover = -1;
    string _title = "Bırak";
    string _subtitle = "";
    string _quality = "";

    public event Action<int>? SliceClicked;

    public void Set(IReadOnlyList<Slice> slices, string title, string subtitle, string quality)
    {
        _slices = slices;
        _title = title;
        _subtitle = subtitle;
        _quality = quality;
        if (_hover >= _slices.Count)
            _hover = -1;
        InvalidateVisual();
    }

    public void Hover(Point local)
    {
        var hit = Hit(local);
        if (hit == _hover)
            return;
        _hover = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Arrow;
        InvalidateVisual();
    }

    public int Hit(Point local) =>
        ActualWidth <= 0 ? -1 : DonutMath.Hit(local.X, local.Y, ActualWidth, ActualHeight, _slices.Count);

    protected override void OnMouseMove(MouseEventArgs e) => Hover(e.GetPosition(this));

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var hit = Hit(e.GetPosition(this));
        if (hit >= 0)
            SliceClicked?.Invoke(hit);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || height < 10)
            return;

        var cx = width / 2;
        var cy = height / 2;
        var (inner, outer) = DonutMath.Radii(width, height);
        var sliceOuter = outer - 6;
        var count = _slices.Count;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var normal = Freeze(Color.FromRgb(42, 49, 64));
        var hot = Freeze(Color.FromRgb(224, 160, 69));
        var ink = Freeze(Color.FromRgb(243, 239, 230));
        var inkDark = Freeze(Color.FromRgb(28, 20, 8));
        var hub = Freeze(Color.FromRgb(16, 18, 24));
        var mute = Freeze(Color.FromRgb(176, 168, 154));

        if (count == 1)
            dc.DrawEllipse(_hover == 0 ? hot : normal, null, new Point(cx, cy), sliceOuter, sliceOuter);
        else if (count > 1)
        {
            var sweep = 360.0 / count;
            for (var i = 0; i < count; i++)
            {
                dc.DrawGeometry(i == _hover ? hot : normal, null, Sector(cx, cy, inner, sliceOuter, i * sweep, sweep, 2.4));
                DrawLabel(dc, _slices[i].Label, cx, cy, (inner + sliceOuter) / 2, i * sweep, typeface, dpi, i == _hover ? inkDark : ink);
            }
        }

        dc.DrawEllipse(hub, new Pen(hot, 2), new Point(cx, cy), inner - 4, inner - 4);
        DrawCenter(dc, cx, cy, inner, typeface, dpi, ink, mute);
    }

    void DrawCenter(DrawingContext dc, double cx, double cy, double inner, Typeface typeface, double dpi, Brush ink, Brush mute)
    {
        var title = Text(_title, typeface, 22, ink, dpi, inner * 1.5);
        var y = cy - title.Height;
        dc.DrawText(title, new Point(cx - title.Width / 2, y));
        y += title.Height + 4;
        if (!string.IsNullOrWhiteSpace(_subtitle))
        {
            var sub = Text(_subtitle, typeface, 12, mute, dpi, inner * 1.5);
            dc.DrawText(sub, new Point(cx - sub.Width / 2, y));
            y += sub.Height + 2;
        }

        if (!string.IsNullOrWhiteSpace(_quality))
        {
            var quality = Text(_quality, typeface, 12, mute, dpi, inner * 1.5);
            dc.DrawText(quality, new Point(cx - quality.Width / 2, y));
        }
    }

    static void DrawLabel(DrawingContext dc, string label, double cx, double cy, double radius, double degrees, Typeface typeface, double dpi, Brush brush)
    {
        var text = Text(label, typeface, 15, brush, dpi, 90);
        var at = DonutMath.PointAt(cx, cy, radius, degrees);
        dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
    }

    static FormattedText Text(string value, Typeface typeface, double size, Brush brush, double dpi, double maxWidth)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, dpi)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = TextAlignment.Center
        };
        return text;
    }

    static StreamGeometry Sector(double cx, double cy, double inner, double outer, double center, double sweep, double gap)
    {
        var start = center - sweep / 2 + gap / 2;
        var end = center + sweep / 2 - gap / 2;
        var large = end - start > 180;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var startPoint = DonutMath.PointAt(cx, cy, outer, start);
            g.BeginFigure(new Point(startPoint.X, startPoint.Y), true, true);
            var outerEnd = DonutMath.PointAt(cx, cy, outer, end);
            g.ArcTo(new Point(outerEnd.X, outerEnd.Y), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            var innerEnd = DonutMath.PointAt(cx, cy, inner, end);
            g.LineTo(new Point(innerEnd.X, innerEnd.Y), true, false);
            var innerStart = DonutMath.PointAt(cx, cy, inner, start);
            g.ArcTo(new Point(innerStart.X, innerStart.Y), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true, false);
        }

        geo.Freeze();
        return geo;
    }

    static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
