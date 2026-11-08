using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClickBoard.Core.Models;

public enum ClipboardContentCategory
{
    PlainText,
    Json,
    Url,
    Sql,
    ShellCommand,
    Code
}

public static partial class ClipboardContentClassifier
{
    private static readonly Regex SqlPattern = new(
        @"^\s*(SELECT\b[\s\S]+\bFROM\b|INSERT\s+INTO\b|UPDATE\s+\S+\s+SET\b|DELETE\s+FROM\b|CREATE\s+(TABLE|VIEW|INDEX|DATABASE)\b|ALTER\s+TABLE\b|WITH\s+\S+\s+AS\s*\()",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private static readonly Regex ShellCommandPattern = new(
        @"^\s*(\$\s+|PS\s+[^>]*>\s*|(?:git|npm|npx|pnpm|yarn|dotnet|docker|kubectl|helm|curl|wget|ssh|scp|pip|python|pytest|cargo|go|make|cmake|winget|choco|brew|sudo|grep|rg|find|mkdir|cd|ls|dir|cat|type|echo|export|setx)\s+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private static readonly Regex CodeKeywordPattern = new(
        @"\b(using|namespace|class|interface|struct|enum|function|const|let|var|return|import|export|def|async|await|public|private|static|void|console\.log)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public static ClipboardContentCategory Classify(ClipKind kind, string? text)
    {
        try { return ClassifyCore(kind, text); }
        catch (RegexMatchTimeoutException) { return ClipboardContentCategory.PlainText; }
    }

    private static ClipboardContentCategory ClassifyCore(ClipKind kind, string? text)
    {
        if (kind != ClipKind.Text || string.IsNullOrWhiteSpace(text))
            return ClipboardContentCategory.PlainText;

        var content = text.Trim();
        if (LooksLikeJson(content))
            return ClipboardContentCategory.Json;

        // Limit classification work while preserving the full clipboard payload.
        if (content.Length > 32768) content = content[..32768];

        if (!content.Any(char.IsWhiteSpace) &&
            Uri.TryCreate(content, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return ClipboardContentCategory.Url;

        if (SqlPattern.IsMatch(content))
            return ClipboardContentCategory.Sql;

        if (ShellCommandPattern.IsMatch(content))
            return ClipboardContentCategory.ShellCommand;

        if (LooksLikeCode(content))
            return ClipboardContentCategory.Code;

        return ClipboardContentCategory.PlainText;
    }

    private static bool LooksLikeJson(string content)
    {
        if (content.Length > 262_144 || content[0] is not ('{' or '['))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeCode(string content)
    {
        if (CodeKeywordPattern.IsMatch(content))
            return true;

        var hasLineBreak = content.Contains('\n') || content.Contains('\r');
        var hasStructure = content.Contains("=>", StringComparison.Ordinal) ||
                           content.Contains("</", StringComparison.Ordinal) ||
                           content.Contains("{", StringComparison.Ordinal) && content.Contains(";", StringComparison.Ordinal) ||
                           content.Contains("def ", StringComparison.Ordinal) ||
                           content.Contains("func ", StringComparison.Ordinal);
        return hasStructure && (hasLineBreak || content.Length < 180);
    }
}
