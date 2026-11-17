using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Halka;

public sealed class AppSettings
{
    public int JpegQuality { get; set; } = 85;
    public string? FfmpegPath { get; set; }
    public string? YtDlpPath { get; set; }
    public bool GestureEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ShowHomeOnStart { get; set; } = true;
    public bool ToldAboutTray { get; set; }
}

public sealed class RecentEntry
{
    public DateTime At { get; set; }
    public string Source { get; set; } = "";
    public string Output { get; set; } = "";
    public bool Ok { get; set; }
    public string Detail { get; set; } = "";
}

static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Halka");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        settings.JpegQuality = Math.Clamp(settings.JpegQuality, 1, 100);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
        ApplyStartup(settings.StartWithWindows);
    }

    public static void ApplyStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null)
            return;
        if (enabled && !string.IsNullOrEmpty(Environment.ProcessPath))
            key.SetValue("Halka", $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue("Halka", throwOnMissingValue: false);
    }
}

static class RecentStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Halka", "recent.json");

    public static List<RecentEntry> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            return JsonSerializer.Deserialize<List<RecentEntry>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Add(IEnumerable<RecentEntry> entries)
    {
        var all = entries.Concat(Load()).Take(40).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
    }
}
