using System.Net.Http;
using System.Text.RegularExpressions;
using ClipboardPal.Core.Abstractions;

namespace ClipboardPal.Core.Services;

public sealed class LinkPreviewService : ILinkPreviewService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex TitleRegex = new(@"<title[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex OgTitleRegex = new(
        @"<meta\s+[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""'](.*?)[""'][^>]*>|<meta\s+[^>]*content\s*=\s*[""'](.*?)[""'][^>]*property\s*=\s*[""']og:title[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex OgDescRegex = new(
        @"<meta\s+[^>]*property\s*=\s*[""']og:description[""'][^>]*content\s*=\s*[""'](.*?)[""'][^>]*>|<meta\s+[^>]*content\s*=\s*[""'](.*?)[""'][^>]*property\s*=\s*[""']og:description[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ClipboardPal/2.0 (+https://localhost)");
        return c;
    }

    public async Task<LinkPreview?> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
                return null;

            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new LinkPreview(url, uri.Host, null);

            var media = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!media.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                !media.Contains("text", StringComparison.OrdinalIgnoreCase))
                return new LinkPreview(url, uri.Host, null);

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (html.Length > 512_000)
                html = html[..512_000];

            var title = MatchGroup(OgTitleRegex, html) ?? MatchGroup(TitleRegex, html);
            title = System.Net.WebUtility.HtmlDecode(title)?.Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = uri.Host;

            var desc = MatchGroup(OgDescRegex, html);
            desc = System.Net.WebUtility.HtmlDecode(desc)?.Trim();
            if (desc is { Length: > 280 })
                desc = desc[..280] + "…";

            return new LinkPreview(url, title, desc);
        }
        catch
        {
            return null;
        }
    }

    private static string? MatchGroup(Regex regex, string html)
    {
        var m = regex.Match(html);
        if (!m.Success) return null;
        for (var i = 1; i < m.Groups.Count; i++)
        {
            if (m.Groups[i].Success && !string.IsNullOrWhiteSpace(m.Groups[i].Value))
                return m.Groups[i].Value;
        }
        return null;
    }
}
