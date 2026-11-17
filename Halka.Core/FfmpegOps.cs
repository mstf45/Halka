using System.Diagnostics;
using System.Text;

namespace Halka.Core;

public static class FfmpegOps
{
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".gif", ".mp3", ".webm", ".wav", ".flac", ".ogg"
    };

    public static string? Find(string? configured)
    {
        if (IsFfmpegExe(configured))
            return Path.GetFullPath(configured!);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
            }
        }

        return null;
    }

    public static void Convert(string ffmpeg, string input, string output, CancellationToken cancellationToken)
    {
        if (!IsFfmpegExe(ffmpeg))
            throw new InvalidOperationException("FFmpeg yolu ffmpeg.exe olmalı.");

        var ext = Path.GetExtension(output);
        if (!Allowed.Contains(ext))
            throw new NotSupportedException("Bu ortam biçimi yazılamıyor.");

        var info = new FileInfo(input);
        if (info.Length > Safety.MaxPackBytes)
            throw new InvalidDataException("Ortam dosyası 8 GB sınırını aşıyor.");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var psi = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(ffmpeg),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(info.FullName)!
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(info.FullName);
        AddOutputArgs(psi, ext);
        psi.ArgumentList.Add(output);

        using var process = new Process { StartInfo = psi };
        var error = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null && error.Length < 4000)
                error.AppendLine(args.Data);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("FFmpeg başlatılamadı.");

            process.BeginErrorReadLine();
            while (!process.WaitForExit(200))
            {
                if (!cancellationToken.IsCancellationRequested)
                    continue;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new OperationCanceledException();
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                TryDelete(output);
                var tail = error.ToString().Trim();
                if (tail.Length > 500)
                    tail = tail[^500..];
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(tail) ? "FFmpeg dönüşümü tamamlayamadı." : tail);
            }
        }
        catch
        {
            TryDelete(output);
            throw;
        }
    }

    static void AddOutputArgs(ProcessStartInfo psi, string ext)
    {
        switch (ext.ToLowerInvariant())
        {
            case ".mp4":
                psi.ArgumentList.Add("-c:v");
                psi.ArgumentList.Add("libx264");
                psi.ArgumentList.Add("-crf");
                psi.ArgumentList.Add("23");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("aac");
                psi.ArgumentList.Add("-movflags");
                psi.ArgumentList.Add("+faststart");
                break;
            case ".gif":
                psi.ArgumentList.Add("-an");
                psi.ArgumentList.Add("-vf");
                psi.ArgumentList.Add("fps=10,scale=480:-1:flags=lanczos");
                break;
            case ".mp3":
                psi.ArgumentList.Add("-vn");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("libmp3lame");
                psi.ArgumentList.Add("-q:a");
                psi.ArgumentList.Add("2");
                break;
            case ".wav":
                psi.ArgumentList.Add("-vn");
                break;
            case ".flac":
                psi.ArgumentList.Add("-vn");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("flac");
                break;
            case ".ogg":
                psi.ArgumentList.Add("-vn");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("libvorbis");
                psi.ArgumentList.Add("-q:a");
                psi.ArgumentList.Add("5");
                break;
            case ".webm":
                psi.ArgumentList.Add("-c:v");
                psi.ArgumentList.Add("libvpx-vp9");
                psi.ArgumentList.Add("-crf");
                psi.ArgumentList.Add("35");
                psi.ArgumentList.Add("-b:v");
                psi.ArgumentList.Add("0");
                psi.ArgumentList.Add("-c:a");
                psi.ArgumentList.Add("libopus");
                break;
        }
    }

    static bool IsFfmpegExe(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        string.Equals(Path.GetFileName(path), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase);

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
}
