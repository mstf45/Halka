using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Halka.Core;

namespace Halka;

public sealed class ToastWindow : Window
{
    readonly TextBlock _title = new()
    {
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White
    };

    readonly TextBlock _body = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        Foreground = new SolidColorBrush(Color.FromRgb(214, 208, 196))
    };

    readonly Button _close;
    DispatcherTimer? _timer;
    bool _finished;

    public event Action? CancelRequested;

    public ToastWindow()
    {
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        _title.Text = "Dönüştürülüyor";
        _body.Text = "Hazırlanıyor";
        _close = new Button
        {
            Content = "Vazgeç",
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _close.Click += CloseClicked;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_title);
        panel.Children.Add(_body);
        panel.Children.Add(_close);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 31, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 160, 69)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = panel
        };
    }

    public void Report(string message) => _body.Text = message;

    public void Finish(IReadOnlyList<ConvertItem> results)
    {
        var bad = results.Count(result => !result.Ok);
        var ok = results.Count - bad;
        _title.Text = bad == 0 ? "Tamam" : "Bitti";
        var lines = string.Join("\n", results.Take(6).Select(result => result.Message));
        if (results.Count > 6)
            lines += "\n…";
        _body.Text = bad == 0 ? lines : $"{ok} tamam, {bad} hata\n{lines}";
        _finished = true;
        _close.Content = "Kapat";
        Place();
        if (bad != 0)
            return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
        _timer.Start();
    }

    void CloseClicked(object sender, RoutedEventArgs e)
    {
        if (!_finished)
            CancelRequested?.Invoke();
        Close();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Place();
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 24;
        Top = area.Bottom - ActualHeight - 24;
    }
}
