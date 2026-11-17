namespace Halka.Core;

public static class DonutMath
{
    public static (double X, double Y) PointAt(double cx, double cy, double radius, double degreesFromTop)
    {
        var radians = degreesFromTop * Math.PI / 180.0;
        return (cx + radius * Math.Sin(radians), cy - radius * Math.Cos(radians));
    }

    public static (double Inner, double Outer) Radii(double width, double height)
    {
        var outer = Math.Min(width, height) / 2.0 - 8.0;
        return (outer * 0.46, outer);
    }

    public static int Hit(double x, double y, double width, double height, int count)
    {
        if (count <= 0 || width <= 0 || height <= 0)
            return -1;

        var cx = width / 2.0;
        var cy = height / 2.0;
        var (inner, outer) = Radii(width, height);
        var dx = x - cx;
        var dy = y - cy;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < inner || distance > outer)
            return -1;

        var degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (degrees < 0)
            degrees += 360.0;

        var sweep = 360.0 / count;
        var shifted = degrees + sweep / 2.0;
        if (shifted >= 360.0)
            shifted -= 360.0;

        var index = (int)(shifted / sweep);
        return index < 0 || index >= count ? -1 : index;
    }
}
