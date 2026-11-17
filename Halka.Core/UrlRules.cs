namespace Halka.Core;

public static class UrlRules
{
    public static bool TryHttpUrl(string? text, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim().Trim('"');
        if (text.Length is 0 or > 2000 || text.Contains('\r') || text.Contains('\n'))
            return false;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrWhiteSpace(parsed.Host) || parsed.Host.Contains('\\') || parsed.Host.Contains('/'))
            return false;

        uri = parsed;
        return true;
    }

    public static string SafeStem(Uri uri)
    {
        var raw = uri.Host + uri.AbsolutePath;
        var chars = raw.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' ? char.ToLowerInvariant(ch) : '-').ToArray();
        var stem = new string(chars).Trim('-');
        while (stem.Contains("--", StringComparison.Ordinal))
            stem = stem.Replace("--", "-", StringComparison.Ordinal);
        if (stem.Length > 80)
            stem = stem[..80].Trim('-');
        return stem.Length == 0 ? "baglanti" : stem;
    }
}
