using System.ComponentModel;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Halka.Core;

namespace Halka;

public partial class MainWindow : Window
{
    bool _loading = true;
    bool _allowClose;
    bool _linkBusy;
    string _offerKind = "video";
    MediaCatalog? _catalog;
    CancellationTokenSource? _linkCts;
    readonly SolidColorBrush _idle = new(Color.FromRgb(60, 65, 77));
    readonly SolidColorBrush _hot = new(Color.FromRgb(224, 160, 69));

    public MainWindow()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        QualitySlider.Value = app.Settings.JpegQuality;
        QualityLabel.Text = app.Settings.JpegQuality.ToString();
        FfmpegBox.Text = app.Settings.FfmpegPath ?? "";
        GestureCheck.IsChecked = app.Settings.GestureEnabled;
        StartupCheck.IsChecked = app.Settings.StartWithWindows;
        HomeCheck.IsChecked = app.Settings.ShowHomeOnStart;
        HookState.Text = app.HookInstalled
            ? "Sürükleme hareketi hazır."
            : "Genel sürükleme kancası kurulamadı. Dosyayı bu pencereye bırakabilirsin.";
        RefreshFfmpeg();
        QualitySlider.ValueChanged += (_, e) =>
        {
            if (_loading)
                return;
            var quality = (int)Math.Round(e.NewValue);
            QualityLabel.Text = quality.ToString();
            app.Settings.JpegQuality = quality;
            app.SaveSettings();
        };
        FfmpegBox.LostFocus += (_, _) => SaveFfmpeg();
        GestureCheck.Checked += (_, _) => SaveFlag();
        GestureCheck.Unchecked += (_, _) => SaveFlag();
        StartupCheck.Checked += (_, _) => SaveFlag();
        StartupCheck.Unchecked += (_, _) => SaveFlag();
        HomeCheck.Checked += (_, _) => SaveFlag();
        HomeCheck.Unchecked += (_, _) => SaveFlag();
        _loading = false;
    }

    public void PrepareClose() => _allowClose = true;

    public void ReloadRecent()
    {
        RecentList.ItemsSource = RecentStore.Load().Select(entry => new RecentRow(entry)).ToList();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var use = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref use, sizeof(int));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose || Application.Current is App { Exiting: true })
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Hide();
        if (Application.Current is App app)
            app.NotifyHidden();
    }

    void DropZone_DragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        DropZone.BorderBrush = ok ? _hot : _idle;
    }

    void DropZone_DragLeave(object sender, DragEventArgs e) => DropZone.BorderBrush = _idle;

    void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = _idle;
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && Application.Current is App app)
            app.ShowDonutFor(files);
    }

    void PickButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app)
            return;
        app.SetDialog(true);
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Title = "Dönüştürülecek dosyalar",
                Filter = "Görseller|*.png;*.jpg;*.jpeg;*.heic;*.heif;*.webp;*.bmp;*.gif|Tüm dosyalar|*.*"
            };
            if (dialog.ShowDialog(this) == true)
                app.ShowDonutFor(dialog.FileNames);
        }
        finally
        {
            app.SetDialog(false);
        }
    }

    void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app)
            return;
        app.SetDialog(true);
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "ffmpeg.exe",
                Filter = "FFmpeg|ffmpeg.exe"
            };
            if (dialog.ShowDialog(this) == true)
            {
                FfmpegBox.Text = dialog.FileName;
                SaveFfmpeg();
            }
        }
        finally
        {
            app.SetDialog(false);
        }
    }

    void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentRow row || !File.Exists(row.Output))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + row.Output + "\"") { UseShellExecute = true });
    }

    void SaveFlag()
    {
        if (_loading || Application.Current is not App app)
            return;
        app.Settings.GestureEnabled = GestureCheck.IsChecked == true;
        app.Settings.StartWithWindows = StartupCheck.IsChecked == true;
        app.Settings.ShowHomeOnStart = HomeCheck.IsChecked == true;
        app.SaveSettings();
    }

    void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            LinkStatus.Text = "Panoda bağlantı yok.";
            return;
        }

        var text = Clipboard.GetText().Trim();
        if (!UrlRules.TryHttpUrl(text, out var uri))
        {
            LinkStatus.Text = "Panoda http veya https bağlantısı yok.";
            return;
        }

        LinkBox.Text = uri.AbsoluteUri;
        _ = LoadFormatsAsync();
    }

    void LinkBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        _ = LoadFormatsAsync();
    }

    void LinkBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => _ = LoadFormatsAsync()));
    }

    void FetchButton_Click(object sender, RoutedEventArgs e) => _ = LoadFormatsAsync();

    void VideoTab_Click(object sender, RoutedEventArgs e)
    {
        _offerKind = "video";
        ApplyOffers();
    }

    void AudioTab_Click(object sender, RoutedEventArgs e)
    {
        _offerKind = "audio";
        ApplyOffers();
    }

    async void PdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (!UrlRules.TryHttpUrl(LinkBox.Text, out var uri))
        {
            LinkStatus.Text = "Geçerli bir http veya https bağlantısı yaz.";
            return;
        }

        await RunLink("PDF", token => PagePdf.SaveAsync(PageView, uri, new Progress<string>(ShowLink), token));
    }

    async void OfferDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_linkBusy || sender is not Button { Tag: MediaOffer offer })
            return;
        if (!UrlRules.TryHttpUrl(LinkBox.Text, out var uri) || Application.Current is not App app)
        {
            LinkStatus.Text = "Geçerli bir http veya https bağlantısı yaz.";
            return;
        }

        var tool = await EnsureToolAsync(app);
        if (tool is null)
            return;

        var ffmpeg = YtDlp.FindFfmpeg(app.Settings.FfmpegPath);
        if (offer.NeedsFfmpeg && ffmpeg is null)
        {
            var answer = MessageBox.Show(
                "Bu biçim sesi videoyla birleştirir veya MP3/WAV yapar. Resmi FFmpeg bir kez indirilecek, yaklaşık 80 MB. Devam edilsin mi?",
                "Halka",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                LinkStatus.Text = "Bu satır FFmpeg olmadan inmez.";
                return;
            }

            try
            {
                BeginLink();
                ffmpeg = await YtDlp.EnsureFfmpegAsync(new Progress<string>(ShowLink), _linkCts!.Token);
                app.Settings.FfmpegPath = ffmpeg;
                FfmpegBox.Text = ffmpeg;
                app.SaveSettings();
                RefreshFfmpeg();
            }
            catch (OperationCanceledException)
            {
                LinkStatus.Text = "Vazgeçildi.";
                return;
            }
            catch (Exception ex)
            {
                LinkStatus.Text = ex.Message;
                return;
            }
            finally
            {
                EndLink();
            }
        }

        await RunLink(offer.Label, token => YtDlp.SaveAsync(tool, ffmpeg, uri, offer, new Progress<string>(ShowLink), token));
    }

    async Task LoadFormatsAsync()
    {
        if (_linkBusy)
            return;
        if (!UrlRules.TryHttpUrl(LinkBox.Text, out var uri) || Application.Current is not App app)
        {
            LinkStatus.Text = "Geçerli bir http veya https bağlantısı yaz.";
            return;
        }

        var tool = await EnsureToolAsync(app);
        if (tool is null)
            return;

        MediaCatalog? catalog = null;
        var ok = await RunLink("Biçim", async token =>
        {
            catalog = await YtDlp.ListAsync(tool, uri, new Progress<string>(ShowLink), token);
            return "";
        });
        if (!ok || catalog is null)
            return;

        _catalog = catalog;
        MediaTitle.Text = string.IsNullOrWhiteSpace(catalog.Title) ? uri.Host : catalog.Title;
        MediaDuration.Text = string.IsNullOrWhiteSpace(catalog.DurationLabel) ? "" : "Süre: " + catalog.DurationLabel;
        FormatHost.Visibility = Visibility.Visible;
        _offerKind = catalog.Offers.Any(item => item.Kind == "video") ? "video" : "audio";
        ApplyOffers();
        LinkStatus.Text = "İstediğin satırda İndir'e bas.";
    }

    async Task<string?> EnsureToolAsync(App app)
    {
        var tool = YtDlp.Find(app.Settings.YtDlpPath);
        if (tool is not null)
            return tool;

        var answer = MessageBox.Show(
            "Video biçimleri için resmi yt-dlp bir kez indirilecek. Adres: github.com/yt-dlp. Devam edilsin mi?",
            "Halka",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return null;

        try
        {
            BeginLink();
            tool = await YtDlp.DownloadOfficialAsync(new Progress<string>(ShowLink), _linkCts!.Token);
            app.Settings.YtDlpPath = tool;
            app.SaveSettings();
            return tool;
        }
        catch (OperationCanceledException)
        {
            LinkStatus.Text = "Vazgeçildi.";
            return null;
        }
        catch (Exception ex)
        {
            LinkStatus.Text = ex.Message;
            return null;
        }
        finally
        {
            EndLink();
        }
    }

    void ApplyOffers()
    {
        var rows = _catalog?.Offers.Where(item => item.Kind == _offerKind).ToList() ?? [];
        FormatList.ItemsSource = rows;
        var videoOn = _offerKind == "video";
        VideoTab.Background = videoOn ? Brushes.White : Brushes.Transparent;
        VideoTab.Foreground = videoOn ? Brushes.Black : Brushes.White;
        AudioTab.Background = videoOn ? Brushes.Transparent : Brushes.White;
        AudioTab.Foreground = videoOn ? Brushes.White : Brushes.Black;
        EmptyFormats.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyFormats.Text = videoOn ? "Bu bağlantıda video biçimi yok." : "Bu bağlantıda ses biçimi yok.";
    }

    void LinkCancel_Click(object sender, RoutedEventArgs e) => _linkCts?.Cancel();

    async Task<bool> RunLink(string kind, Func<CancellationToken, Task<string>> work)
    {
        if (_linkBusy)
            return false;
        BeginLink();
        try
        {
            var output = await work(_linkCts!.Token);
            if (string.IsNullOrEmpty(output))
                return true;
            LinkStatus.Text = kind + " kaydedildi: " + output;
            RecentStore.Add(
            [
                new RecentEntry
                {
                    At = DateTime.Now,
                    Source = LinkBox.Text.Trim(),
                    Output = output,
                    Ok = true,
                    Detail = Path.GetFileName(output)
                }
            ]);
            ReloadRecent();
            return true;
        }
        catch (OperationCanceledException)
        {
            LinkStatus.Text = "Vazgeçildi.";
            return false;
        }
        catch (Exception ex)
        {
            LinkStatus.Text = ex.Message;
            RecentStore.Add(
            [
                new RecentEntry
                {
                    At = DateTime.Now,
                    Source = LinkBox.Text.Trim(),
                    Output = "",
                    Ok = false,
                    Detail = ex.Message
                }
            ]);
            ReloadRecent();
            return false;
        }
        finally
        {
            EndLink();
        }
    }

    void BeginLink()
    {
        _linkBusy = true;
        _linkCts = new CancellationTokenSource();
        PdfButton.IsEnabled = false;
        FetchButton.IsEnabled = false;
        FormatList.IsEnabled = false;
        LinkCancel.Visibility = Visibility.Visible;
    }

    void EndLink()
    {
        _linkBusy = false;
        _linkCts?.Dispose();
        _linkCts = null;
        PdfButton.IsEnabled = true;
        FetchButton.IsEnabled = true;
        FormatList.IsEnabled = true;
        LinkCancel.Visibility = Visibility.Collapsed;
        PageView.Height = 0;
    }

    void ShowLink(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => LinkStatus.Text = message);
            return;
        }

        LinkStatus.Text = message;
    }

    void SaveFfmpeg()
    {
        if (_loading || Application.Current is not App app)
            return;
        var text = FfmpegBox.Text.Trim();
        app.Settings.FfmpegPath = string.IsNullOrEmpty(text) ? null : text;
        app.SaveSettings();
        RefreshFfmpeg();
    }

    void RefreshFfmpeg()
    {
        var found = YtDlp.FindFfmpeg(FfmpegBox.Text.Trim());
        FfmpegState.Text = found is null
            ? "FFmpeg yok. MP3 ve yüksek çözünürlük ilk indirmede kurulur."
            : "FFmpeg hazır: " + found;
    }
}

public sealed class RecentRow
{
    public RecentRow(RecentEntry entry)
    {
        Output = entry.Output;
        Title = string.IsNullOrWhiteSpace(entry.Detail) ? Path.GetFileName(entry.Source) : entry.Detail;
        When = entry.At.ToString("g");
        Mark = entry.Ok ? "●" : "!";
    }

    public string Output { get; }
    public string Title { get; }
    public string When { get; }
    public string Mark { get; }
}
