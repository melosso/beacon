using System.Net;
using System.Text.RegularExpressions;

namespace Beacon;

internal static partial class LoginFooterParser
{
    [GeneratedRegex(@"\*\*(.*?)\*\*")]      private static partial Regex BoldAsterisks();
    [GeneratedRegex(@"__(.*?)__")]           private static partial Regex BoldUnderscores();
    [GeneratedRegex(@"\*(.*?)\*")]           private static partial Regex ItalicAsterisks();
    [GeneratedRegex(@"_(.*?)_")]             private static partial Regex ItalicUnderscores();
    [GeneratedRegex(@"\[(.*?)\]\((.*?)\)")]  private static partial Regex MarkdownLink();

    internal static string ParseMarkdown(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return string.Empty;

        // Encode first — kills all raw HTML/script injection
        var html = WebUtility.HtmlEncode(md);

        html = BoldAsterisks().Replace(html,     "<strong>$1</strong>");
        html = BoldUnderscores().Replace(html,   "<strong>$1</strong>");
        html = ItalicAsterisks().Replace(html,   "<em>$1</em>");
        html = ItalicUnderscores().Replace(html, "<em>$1</em>");

        html = MarkdownLink().Replace(html, m =>
        {
            // text is already HTML-encoded (may contain <strong>/<em> from earlier passes)
            var text = m.Groups[1].Value;
            // URL was HTML-encoded with the rest; decode it so we inspect the real value
            var rawUrl = WebUtility.HtmlDecode(m.Groups[2].Value);

            // Reject URLs with characters that break out of a double-quoted href attribute
            if (rawUrl.IndexOfAny(['"', '\'', '<', '>']) >= 0)
                return text;

            var isExternal =
                rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) ||
                rawUrl.StartsWith("//",       StringComparison.Ordinal);
            var isSafe = isExternal ||
                rawUrl.StartsWith("/",       StringComparison.Ordinal) ||
                rawUrl.StartsWith("#",       StringComparison.Ordinal) ||
                rawUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.StartsWith("tel:",    StringComparison.OrdinalIgnoreCase);

            if (!isSafe) return text;

            // Re-encode only & for valid HTML in href; " is already rejected above
            var safeUrl = rawUrl.Replace("&", "&amp;");
            var attrs = isExternal ? " target=\"_blank\" rel=\"noopener noreferrer\"" : string.Empty;
            return $"<a href=\"{safeUrl}\"{attrs}>{text}</a>";
        });

        return html;
    }
}
