using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using Halka.Core;

namespace Halka;

static class YtDlp
{
    const string OfficialUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    const string FfmpegZipUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public static string? Find(string? configured)
    {
        if (IsTool(configured))
            return Path.GetFullPath(configured!);

        var bundled = Path.Combine(ToolsDir(), "yt-dlp.exe");
        if (File.Exists(bundled))
            return bundled;

        return FindOnPath("yt-dlp.exe");
    }

    public static string? FindFfmpeg(string? configured)
    {
        var found = FfmpegOps.Find(configured);
        if (found is not null)
            return found;

        var bundled = Path.Combine(ToolsDir(), "ffmpeg.exe");
        if (File.Exists(bundled) && new FileInfo(bundled).Length > 1_000_000)
            return bundled;
        return null;
    }

    public static async Task<string> DownloadOfficialAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ToolsDir());
        var destination = Path.Combine(ToolsDir(), "yt-dlp.exe");
        var partial = destination + ".part";
        await DownloadHttpsAsync(OfficialUrl, partial, 80_000_000, "yt-dlp indiriliyor", progress, cancellationToken);
        var info = new FileInfo(partial);
        if (info.Length < 1_000_000)
        {
            TryDelete(partial);
            throw new InvalidOperationException("İndirilen yt-dlp dosyası eksik.");
        }

        Replace(partial, destination);
        return destination;
    }

    public static async Task<string> EnsureFfmpegAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var existing = FindFfmpeg(null);
        if (existing is not null)
            return existing;

        Directory.CreateDirectory(ToolsDir());
        var zip = Path.Combine(ToolsDir(), "ffmpeg.zip.part");
        var exe = Path.Combine(ToolsDir(), "ffmpeg.exe");
        try
        {
            await DownloadHttpsAsync(FfmpegZipUrl, zip, 250_000_000, "FFmpeg indiriliyor", progress, cancellationToken);
            progress?.Report("FFmpeg kuruluyor…");
            ExtractFfmpeg(zip, exe);
        }
        finally
        {
            TryDelete(zip);
        }

        if (!File.Exists(exe) || new FileInfo(exe).Length < 1_000_000)
            throw new InvalidOperationException("FFmpeg kurulamadı.");
        return exe;
    }

    public static Task<MediaCatalog> ListAsync(string tool, Uri url, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            progress?.Report("Biçimler getiriliyor…");
            var (code, stdout, stderr) = Run(tool, psi =>
            {
                psi.ArgumentList.Add("--no-playlist");
                psi.ArgumentList.Add("--no-warnings");
                psi.ArgumentList.Add("--skip-download");
                AddCommon(psi);
                psi.ArgumentList.Add("-J");
                psi.ArgumentList.Add(url.AbsoluteUri);
            }, progress, cancellationToken, 180_000);

            if (code != 0 || string.IsNullOrWhiteSpace(stdout))
                throw new InvalidOperationException(Explain(stderr));

            try
            {
                return FormatList.Parse(ExtractJson(stdout));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException("Biçim listesi okunamadı.");
            }
        }, cancellationToken);
    }

    public static Task<string> SaveAsync(
        string tool,
        string? ffmpeg,
        Uri url,
        MediaOffer offer,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (!SafeSelector(offer.Selector))
                throw new InvalidOperationException("Bu biçim kullanılamıyor.");
            if (offer.MergeExt is not null && offer.MergeExt is not ("mp4" or "webm" or "mkv"))
                throw new InvalidOperationException("Bu biçim kullanılamıyor.");
            if (offer.AudioFormat is not null && offer.AudioFormat is not ("mp3" or "wav" or "m4a"))
                throw new InvalidOperationException("Bu ses biçimi kullanılamıyor.");

            var hasFfmpeg = !string.IsNullOrWhiteSpace(ffmpeg) && File.Exists(ffmpeg);
            if (offer.NeedsFfmpeg && !hasFfmpeg)
                throw new InvalidOperationException("Bu biçim için FFmpeg gerekli.");

            var folder = OutputFolder();
            var before = Directory.Exists(folder)
                ? Directory.GetFiles(folder).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(folder);

            string? lastError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt == 1)
                {
                    if (!hasFfmpeg)
                        break;
                    DeleteNew(folder, before);
                    progress?.Report("Adres reddedildi, FFmpeg ile yeniden deneniyor…");
                }

                var viaFfmpeg = attempt == 1;
                var (code, stdout, stderr) = Run(tool, psi =>
                {
                    psi.WorkingDirectory = folder;
                    psi.ArgumentList.Add("--newline");
                    psi.ArgumentList.Add("--no-mtime");
                    psi.ArgumentList.Add("--restrict-filenames");
                    psi.ArgumentList.Add("--windows-filenames");
                    psi.ArgumentList.Add("--no-playlist");
                    psi.ArgumentList.Add("-o");
                    psi.ArgumentList.Add(Path.Combine(folder, "%(title).80s.%(ext)s"));
                    psi.ArgumentList.Add("--print");
                    psi.ArgumentList.Add("after_move:filepath");
                    AddCommon(psi);
                    if (hasFfmpeg)
                    {
                        psi.ArgumentList.Add("--ffmpeg-location");
                        psi.ArgumentList.Add(Path.GetFullPath(ffmpeg!));
                    }

                    if (viaFfmpeg)
                    {
                        psi.ArgumentList.Add("--downloader");
                        psi.ArgumentList.Add("ffmpeg");
                    }

                    if (offer.AudioFormat is not null)
                    {
                        psi.ArgumentList.Add("-f");
                        psi.ArgumentList.Add(offer.Selector);
                        psi.ArgumentList.Add("-x");
                        psi.ArgumentList.Add("--audio-format");
                        psi.ArgumentList.Add(offer.AudioFormat);
                    }
                    else
                    {
                        psi.ArgumentList.Add("-f");
                        psi.ArgumentList.Add(offer.Selector);
                        if (offer.MergeExt is not null)
                        {
                            psi.ArgumentList.Add("--merge-output-format");
                            psi.ArgumentList.Add(offer.MergeExt);
                        }
                    }

                    psi.ArgumentList.Add(url.AbsoluteUri);
                }, progress, cancellationToken, 0);

                var printed = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var created = Directory.GetFiles(folder)
                    .Where(file => !before.Contains(file) && !file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var printedFile = printed.LastOrDefault(File.Exists);
                var output = printedFile ?? created.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (code == 0 && output is not null)
                    return output;

                lastError = Explain(stderr);
                if (!lastError.Contains("403", StringComparison.Ordinal))
                    break;
            }

            DeleteNew(folder, before);
            throw new InvalidOperationException(lastError ?? "Video kaydedilemedi.");
        }, cancellationToken);
    }

    public static string OutputFolder()
    {
        var folder = Path.Combine(DownloadsFolder(), "Halka");
        Directory.CreateDirectory(folder);
        return folder;
    }

    static (int Code, string Stdout, string Stderr) Run(
        string tool,
        Action<ProcessStartInfo> configure,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        int timeoutMs)
    {
        if (!IsTool(tool))
            throw new InvalidOperationException("yt-dlp.exe bulunamadı.");

        var psi = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(tool),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        configure(psi);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;
            if (args.Data.StartsWith("[download]", StringComparison.Ordinal))
                progress?.Report(Trim(args.Data));
            else if (stdout.Length < 8_000_000)
                stdout.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;
            if (args.Data.StartsWith("[download]", StringComparison.Ordinal) || args.Data.StartsWith("[Merger]", StringComparison.Ordinal) || args.Data.StartsWith("[ExtractAudio]", StringComparison.Ordinal))
            {
                progress?.Report(Trim(args.Data));
                return;
            }

            if (stderr.Length < 4000)
                stderr.AppendLine(args.Data.Trim());
        };

        if (!process.Start())
            throw new InvalidOperationException("yt-dlp başlatılamadı.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var deadline = timeoutMs > 0 ? Environment.TickCount64 + timeoutMs : long.MaxValue;
        while (!process.WaitForExit(300))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Kill(process);
                throw new OperationCanceledException();
            }

            if (Environment.TickCount64 > deadline)
            {
                Kill(process);
                throw new InvalidOperationException("Biçim listesi zaman aşımına uğradı.");
            }
        }

        process.WaitForExit();
        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    static void AddCommon(ProcessStartInfo psi)
    {
        AddRuntime(psi);
        psi.ArgumentList.Add("--impersonate");
        psi.ArgumentList.Add("chrome");
        psi.ArgumentList.Add("--force-ipv4");
        psi.ArgumentList.Add("--retries");
        psi.ArgumentList.Add("10");
        psi.ArgumentList.Add("--fragment-retries");
        psi.ArgumentList.Add("10");
        psi.ArgumentList.Add("--extractor-retries");
        psi.ArgumentList.Add("3");
    }

    static void AddRuntime(ProcessStartInfo psi)
    {
        var node = FindNode();
        if (node is null)
            return;
        var dir = Path.GetDirectoryName(node);
        if (!string.IsNullOrEmpty(dir))
        {
            string? current = null;
            string? key = null;
            foreach (System.Collections.DictionaryEntry entry in psi.EnvironmentVariables)
            {
                var name = entry.Key?.ToString();
                if (name is not null && name.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                {
                    key = name;
                    current = entry.Value?.ToString();
                    break;
                }
            }

            psi.EnvironmentVariables[key ?? "PATH"] = dir + Path.PathSeparator + (current ?? "");
        }

        psi.ArgumentList.Add("--js-runtimes");
        psi.ArgumentList.Add("node");
        psi.ArgumentList.Add("--remote-components");
        psi.ArgumentList.Add("ejs:github");
    }

    static async Task DownloadHttpsAsync(string url, string partial, long maxBytes, string label, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(label + "…");
        TryDelete(partial);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        var next = new Uri(url);
        for (var hop = 0; hop < 5; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureHost(next);
            using var response = await client.GetAsync(next, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect)
            {
                next = response.Headers.Location ?? throw new InvalidOperationException("İndirme adresi eksik.");
                if (!next.IsAbsoluteUri)
                    next = new Uri(response.RequestMessage?.RequestUri!, next);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length is > 0 && length > maxBytes)
                throw new InvalidOperationException("İndirme beklenenden büyük.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(partial);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    throw new InvalidOperationException("İndirme beklenenden büyük.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (length is > 0 && total % (512 * 1024) < buffer.Length)
                    progress?.Report($"{label}… {total * 100 / length.Value}%");
            }

            if (total < 100_000)
                throw new InvalidOperationException("İndirme eksik kaldı.");
            return;
        }

        throw new InvalidOperationException("İndirme tamamlanamadı.");
    }

    static void ExtractFfmpeg(string zipPath, string destination)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? chosen = null;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.Contains("..", StringComparison.Ordinal) || entry.Length < 1_000_000)
                continue;
            if (!name.EndsWith("/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (chosen is null || name.Contains("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                chosen = entry;
        }

        if (chosen is null)
            throw new InvalidOperationException("FFmpeg paketi beklenen dosyayı içermiyor.");

        var temp = destination + ".part";
        TryDelete(temp);
        using (var input = chosen.Open())
        using (var output = File.Create(temp))
            input.CopyTo(output);
        Replace(temp, destination);
    }

    static string? FindNode()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var candidate = Path.Combine(root, "nodejs", "node.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return FindOnPath("node.exe");
    }

    static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
            }
        }

        return null;
    }

    static string DownloadsFolder()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) == 0 && pointer != IntPtr.Zero)
        {
            try
            {
                var path = System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer);
                if (!string.IsNullOrWhiteSpace(path))
                    return path;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    static string ToolsDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Halka", "tools");

    static bool IsTool(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        string.Equals(Path.GetFileName(path), "yt-dlp.exe", StringComparison.OrdinalIgnoreCase);

    static bool SafeSelector(string value) =>
        value.Length is > 0 and <= 80 &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '+' or '-');

    static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Biçim listesi okunamadı.");
        return text[start..(end + 1)];
    }

    static string Explain(string raw)
    {
        var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.LastOrDefault(item => item.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
                   ?? lines.LastOrDefault()
                   ?? "";
        if (line.Contains("JavaScript runtime", StringComparison.OrdinalIgnoreCase))
            return "YouTube bu bağlantı için Node.js istiyor. Node kurulu değil.";
        if (line.Contains("Requested format is not available", StringComparison.OrdinalIgnoreCase))
            return "Bu biçim kalktı. Biçimleri yeniden getir.";
        if (line.Contains("403", StringComparison.Ordinal))
            return "YouTube bu MP4 adresini reddetti (403). İndir'e bir kez daha bas.";
        if (line.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            return "Bu biçim FFmpeg istiyor. İndir'e basınca kurulum sorulur.";
        var marker = line.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            line = line[(marker + 6)..].Trim();
        if (string.IsNullOrWhiteSpace(line))
            return "Video kaydedilemedi.";
        return line.Length > 220 ? line[..220] : line;
    }

    static string Trim(string line)
    {
        line = line.Trim();
        return line.Length > 180 ? line[..180] : line;
    }

    static void EnsureHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Araç yalnızca https ile indirilir.");
        var host = uri.IdnHost;
        if (host is not ("github.com" or "objects.githubusercontent.com" or "release-assets.githubusercontent.com" or "github-releases.githubusercontent.com"))
            throw new InvalidOperationException("İndirme adresi resmi GitHub dışında.");
    }

    static void DeleteNew(string folder, HashSet<string> before)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.GetFiles(folder))
        {
            if (!before.Contains(file))
                TryDelete(file);
        }
    }

    static void Replace(string partial, string destination)
    {
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(partial, destination);
    }

    static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
