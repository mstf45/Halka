namespace Halka.Core;

public sealed class GestureTracker
{
    public const double Threshold = 18;

    public bool Armed { get; set; }
    public bool Pressed { get; private set; }
    public bool Open { get; set; }

    double _originX;
    double _originY;
    bool _hasOrigin;

    public void Press(double x, double y)
    {
        Pressed = true;
        _originX = x;
        _originY = y;
        _hasOrigin = true;
    }

    public void Release()
    {
        Pressed = false;
        _hasOrigin = false;
    }

    public bool Moved(double x, double y)
    {
        if (!Armed || !Pressed || Open || !_hasOrigin)
            return false;

        var dx = x - _originX;
        var dy = y - _originY;
        return dx * dx + dy * dy >= Threshold * Threshold;
    }
}
