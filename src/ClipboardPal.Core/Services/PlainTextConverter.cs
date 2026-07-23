using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardPal.Core.Services;

public static class PlainTextConverter
{
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex WsRegex = new(@"[ \t]+\r?\n", RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@"[ \t]{2,}", RegexOptions.Compiled);

    public static string ToPlain(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var text = WebUtility.HtmlDecode(input);
        text = TagRegex.Replace(text, " ");
        text = text.Replace("\u00a0", " ");
        text = WsRegex.Replace(text, "\n");
        text = MultiSpace.Replace(text, " ");
        return text.Trim();
    }

    public static bool LooksLikeHtml(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("<p>", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("<span", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("&nbsp;", StringComparison.OrdinalIgnoreCase));
}

public static class UrlDetector
{
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? FirstUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = UrlRegex.Match(text.Trim());
        return m.Success ? m.Value.TrimEnd('.', ',', ')', ']', '>', '"') : null;
    }
}
