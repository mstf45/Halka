using System.IO;
using Halka.Core;
using Microsoft.Web.WebView2.Core;

namespace Halka;

static class PagePdf
{
    public static async Task<string> SaveAsync(Microsoft.Web.WebView2.Wpf.WebView2 view, Uri url, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Sayfa açılıyor…");
        var folder = YtDlp.OutputFolder();
        var output = Unique(Path.Combine(folder, UrlRules.SafeStem(url) + ".pdf"));
        if (view.CoreWebView2 is null)
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Halka", "webview");
            Directory.CreateDirectory(data);
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            await view.EnsureCoreWebView2Async(environment);
        }

        var core = view.CoreWebView2 ?? throw new InvalidOperationException("Sayfa görüntüleyici açılamadı.");
        if (view.Tag is not true)
        {
            view.Tag = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
        }

        var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
                args.Cancel = true;
        }

        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            navigated.TrySetResult(args.IsSuccess);
        }

        core.NavigationStarting += Starting;
        core.NavigationCompleted += Completed;
        try
        {
            view.Height = 220;
            core.Navigate(url.AbsoluteUri);
            var finished = await Task.WhenAny(navigated.Task, Task.Delay(TimeSpan.FromSeconds(45), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (finished != navigated.Task || !await navigated.Task)
                throw new InvalidOperationException("Sayfa açılamadı.");

            progress?.Report("PDF yazılıyor…");
            await Task.Delay(1200, cancellationToken);
            var settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            var ok = await core.PrintToPdfAsync(output, settings);
            if (!ok || !File.Exists(output))
                throw new InvalidOperationException("PDF kaydedilemedi.");
            return output;
        }
        catch
        {
            try
            {
                if (File.Exists(output))
                    File.Delete(output);
            }
            catch
            {
            }

            throw;
        }
        finally
        {
            core.NavigationStarting -= Starting;
            core.NavigationCompleted -= Completed;
            view.Height = 0;
        }
    }

    static string Unique(string path)
    {
        if (!File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Uygun dosya adı bulunamadı.");
    }
}
