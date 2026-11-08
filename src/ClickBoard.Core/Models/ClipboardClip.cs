namespace ClickBoard.Core.Models;

public sealed record ClipboardClip
{
    private ClipboardContentCategory? _contentCategory;

    public long Id { get; init; }
    public ClipKind Kind { get; init; }
    public string PreviewText { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? TextContent { get; init; }
    public string? HtmlContent { get; init; }
    public string? RtfContent { get; init; }
    public byte[]? ImageBytes { get; init; }
    public byte[]? ThumbnailBytes { get; init; }
    public bool IsPayloadLoaded { get; init; } = true;
    public ClipboardContentCategory? Category { get; init; }
    public string? Collection { get; init; }
    public string? Tags { get; init; }
    public string? FilesJson { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string? SourceApp { get; init; }
    public bool IsFavorite { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string DetailText => TextContent ?? PreviewText;
    public ClipboardContentCategory ContentCategory =>
        Category ?? (_contentCategory ??= ClipboardContentClassifier.Classify(Kind, TextContent));
    public bool IsDeveloperContent => ContentCategory is ClipboardContentCategory.Json
        or ClipboardContentCategory.Url
        or ClipboardContentCategory.Sql
        or ClipboardContentCategory.ShellCommand
        or ClipboardContentCategory.Code;

    public string KindLabel => Kind switch
    {
        ClipKind.Image => "Imagen",
        ClipKind.Files => "Archivos",
        _ => ContentCategory switch
        {
            ClipboardContentCategory.Json => "JSON",
            ClipboardContentCategory.Url => "Enlace",
            ClipboardContentCategory.Sql => "SQL",
            ClipboardContentCategory.ShellCommand => "Terminal",
            ClipboardContentCategory.Code => "Código",
            _ => "Texto"
        }
    };

    public string ShortPreview => DisplayTitle.Length > 190
        ? string.Concat(DisplayTitle.AsSpan(0, 187), "…")
        : DisplayTitle;
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? PreviewText : Title;
}
