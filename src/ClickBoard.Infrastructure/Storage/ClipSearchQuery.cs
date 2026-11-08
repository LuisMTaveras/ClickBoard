using System.Globalization;
using System.Text.RegularExpressions;

namespace ClickBoard.Infrastructure.Storage;

public sealed record ClipSearchQuery(string Text, IReadOnlyList<(string Field, string Value)> Filters)
{
    public static ClipSearchQuery Parse(string query)
    {
        var filters = new List<(string, string)>();
        var text = Regex.Replace(
            query,
            "(?<!\\S)(tipo|etiqueta|app|antes|proyecto):(?:\"([^\"]+)\"|(\\S+))",
            match =>
            {
                var field = match.Groups[1].Value.ToLowerInvariant();
                var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                if (field == "antes" && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    return match.Value;
                filters.Add((field, value));
                return " ";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        return new(text.Trim(), filters);
    }
}
