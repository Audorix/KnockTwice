using System.Text;
using System.Text.RegularExpressions;

namespace Audorix.KnockTwice.Application.Common;

/// <summary>
/// Message text helpers (Phase 19, ADR-033). Messages keep the Markdown-style text people type
/// (<c>**bold**</c>, <c>*italic*</c>, <c>~~strike~~</c>, <c>`code`</c>, code blocks, quotes, lists,
/// <c>[text](url)</c>); clients render it. Places that show plain text (email, push, reply previews)
/// use <see cref="ToPlain"/>, which drops the markers but keeps the words and link targets.
/// </summary>
public static partial class MessageText
{
    [GeneratedRegex(@"\[([^\]\n]+)\]\((https?://[^\s)]+|mailto:[^\s)]+)\)", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Link();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Strong();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Strike();

    [GeneratedRegex(@"(?<![\p{L}\p{N}*_])([*_])(?=\S)(.+?)(?<=\S)\1(?![\p{L}\p{N}*_])", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"`([^`\n]+)`", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Code();

    [GeneratedRegex(@"^\s{0,3}>\s?", RegexOptions.CultureInvariant, 500)]
    private static partial Regex Quote();

    /// <summary>The text without formatting markers: words, code, link texts (with their address) and line breaks.</summary>
    public static string ToPlain(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new StringBuilder(text.Length);
        var inFence = false;
        foreach (var raw in text.Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue; // the fence line (and its language name) is not content
            }

            var line = raw;
            if (!inFence)
            {
                line = Quote().Replace(line, string.Empty);
                line = Code().Replace(line, "$1");
                line = Link().Replace(line, m => string.Equals(m.Groups[1].Value, m.Groups[2].Value, StringComparison.Ordinal)
                    ? m.Groups[2].Value
                    : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
                line = Strong().Replace(line, "$2");
                line = Strike().Replace(line, "$1");
                line = Emphasis().Replace(line, "$2");
            }

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(line);
        }

        return result.ToString().Trim();
    }

    /// <summary>One line of plain text of at most <paramref name="maxLength"/> characters (with an ellipsis), or null for empty text.</summary>
    public static string? Excerpt(string? text, int maxLength = 140)
    {
        var line = ToPlain(text).ReplaceLineEndings(" ").Trim();
        while (line.Contains("  ", StringComparison.Ordinal))
        {
            line = line.Replace("  ", " ", StringComparison.Ordinal);
        }

        return line.Length == 0 ? null : line.Length <= maxLength ? line : line[..maxLength] + "…";
    }
}
