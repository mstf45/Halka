namespace Halka;

sealed class TrayHost : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon _icon;
    readonly System.Drawing.Icon _iconImage;

    public TrayHost(Action show, Action exit)
    {
        _iconImage = MakeIcon();
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _iconImage,
            Visible = true,
            Text = "Halka — çevrimdışı dönüştürücü"
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ana sayfa", null, (_, _) => show());
        menu.Items.Add("Çıkış", null, (_, _) => exit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => show();
    }

    public void Balloon(string message) =>
        _icon.ShowBalloonTip(4000, "Halka", message, System.Windows.Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }

    static System.Drawing.Icon MakeIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var gold = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 224, 160, 69));
            using var hole = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 20, 22, 28));
            g.FillEllipse(gold, 1, 1, 30, 30);
            g.FillEllipse(hole, 10, 10, 12, 12);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
