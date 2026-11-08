using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using ClickBoard.Core.Models;
using ClickBoard.Core.Services;
using ClickBoard.Infrastructure.Storage;

namespace ClickBoard.Infrastructure.Windows;

public sealed class ClipboardCaptureService(
    IClipboardRepository repository,
    ForegroundWindowTracker foregroundWindowTracker)
{
    private const string ExclusionFormat = "ExcludeClipboardContentFromMonitorProcessing";
    public int MaxFileSizeMb { get; set; } = 50;
    public string ExcludedApplications { get; set; } = string.Empty;

    public async Task<bool> TryCaptureAsync(CancellationToken cancellationToken = default)
    {
        ClipboardClip? clip = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                clip = ReadCurrentClipboard();
                break;
            }
            catch (Exception exception) when (
                exception is COMException or InvalidOperationException && attempt < 3)
            {
                await Task.Delay(45 * (attempt + 1), cancellationToken);
            }
        }

        if (clip is null)
            return false;

        if (clip.Kind == ClipKind.Files && clip.FilesJson is not null)
        {
            await Task.Run(() => CaptureFilesAsync(clip, cancellationToken), cancellationToken);
            return true;
        }

        if (!repository.TemporaryMode)
        {
            using var quota = await StorageQuota.AcquireAsync((clip.ImageBytes?.LongLength ?? 0) + (long)(clip.TextContent?.Length ?? 0) * 2 + (long)(clip.HtmlContent?.Length ?? 0) * 2 + (long)(clip.RtfContent?.Length ?? 0) * 2 + 65536, cancellationToken);
            await repository.SaveAsync(clip, cancellationToken);
            return true;
        }
        await repository.SaveAsync(clip, cancellationToken);
        return true;
    }

    private async Task CaptureFilesAsync(ClipboardClip clip, CancellationToken cancellationToken)
    {
        var sourcePaths = JsonSerializer.Deserialize<string[]>(clip.FilesJson!) ?? [];
        var maximumBytes = (long)Math.Clamp(MaxFileSizeMb, 1, 10240) * 1024 * 1024;
        foreach (var source in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(source))
                throw new ClipboardCaptureException("La copia de carpetas todavía no está disponible; selecciona archivos individuales.");
            var file = new FileInfo(source);
            if (!file.Exists)
                throw new ClipboardCaptureException($"No se encontró el archivo «{file.Name}».");
            if (file.Length > maximumBytes)
                throw new ClipboardCaptureException($"«{file.Name}» supera el límite de {MaxFileSizeMb} MB por archivo.");
        }
        if ( sourcePaths.Length == 0 )
            throw new ClipboardCaptureException("No se encontraron archivos para guardar.");

        if (repository.TemporaryMode)
        {
            var tempHash = await ComputeFilesHashAsync(sourcePaths, maximumBytes, cancellationToken);
            var tempPreviewImage = sourcePaths.Length == 1 ? FilePreviewHelper.TryExtractPreviewImage(sourcePaths[0]) : null;
            var tempClip = clip with
            {
                PreviewText = FilePreviewHelper.BuildShortPreview(sourcePaths),
                TextContent = FilePreviewHelper.BuildFilesSummary(sourcePaths),
                ImageBytes = tempPreviewImage,
                ThumbnailBytes = tempPreviewImage is null ? null : ImageThumbnail.Create(tempPreviewImage),
                ContentHash = tempHash
            };
            await repository.SaveAsync(tempClip, cancellationToken);
            return;
        }

        var totalSourceBytes = sourcePaths.Sum(path => new FileInfo(path).Length);
        IDisposable? quota = null;
        try
        {
            quota = await StorageQuota.AcquireAsync(totalSourceBytes + 65536, cancellationToken);
        }
        catch (StorageQuotaException)
        {
            // If quota is full, check if this exact file set is already stored and intact.
            var existingHash = await ComputeFilesHashAsync(sourcePaths, maximumBytes, cancellationToken);
            if (!await repository.SaveAsync(clip with { ContentHash = existingHash }, cancellationToken))
                return;
            throw;
        }

        using (quota)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "files");
            var captureDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var markerPath = Path.Combine(captureDirectory, ".clipforge-owned");
            var markerCreated = false;
            var copiedFiles = new List<string>();
            var contents = new List<string>();
            var itemDirectories = new List<string>();
            try
            {
                Directory.CreateDirectory(captureDirectory);
                await using (var marker = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    markerCreated = true;
                    var markerBytes = Encoding.UTF8.GetBytes("ClipForge snapshot v1:" + Path.GetFileName(captureDirectory));
                    await marker.WriteAsync(markerBytes, cancellationToken);
                }
                for (var index = 0; index < sourcePaths.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var itemDirectory = Path.Combine(captureDirectory, index.ToString("D4"));
                    Directory.CreateDirectory(itemDirectory);
                    itemDirectories.Add(itemDirectory);
                    var destination = Path.Combine(itemDirectory, Path.GetFileName(sourcePaths[index]));
                    await using var input = new FileStream(sourcePaths[index], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    copiedFiles.Add(destination);
                    var buffer = new byte[81920];
                    long total = 0;
                    using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > maximumBytes)
                            throw new ClipboardCaptureException($"«{Path.GetFileName(sourcePaths[index])}» supera el límite de {MaxFileSizeMb} MB por archivo.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        fileHash.AppendData(buffer, 0, read);
                    }
                    contents.Add(Path.GetFileName(sourcePaths[index]) + ":" + Convert.ToHexString(fileHash.GetHashAndReset()));
                }
                var previewImage = copiedFiles.Count == 1 ? FilePreviewHelper.TryExtractPreviewImage(copiedFiles[0]) : null;
                var savedClip = clip with
                {
                    PreviewText = FilePreviewHelper.BuildShortPreview(copiedFiles),
                    TextContent = FilePreviewHelper.BuildFilesSummary(copiedFiles),
                    ImageBytes = previewImage,
                    ThumbnailBytes = previewImage is null ? null : ImageThumbnail.Create(previewImage),
                    FilesJson = JsonSerializer.Serialize(copiedFiles),
                    ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contents.OrderBy(value => value, StringComparer.Ordinal)))))
                };
                if (!await repository.SaveAsync(savedClip, cancellationToken))
                    OwnedFileStore.DeleteCopies(savedClip.FilesJson);
            }
            catch
            {
                foreach (var path in copiedFiles)
                    try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                foreach (var directory in itemDirectories)
                    try { Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (markerCreated)
                    try { File.Delete(markerPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { Directory.Delete(captureDirectory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }

    private async Task<string> ComputeFilesHashAsync(string[] sourcePaths, long maximumBytes, CancellationToken cancellationToken)
    {
        var contents = new List<string>(sourcePaths.Length);
        var buffer = new byte[81920];
        foreach (var path in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                    throw new ClipboardCaptureException($"«{Path.GetFileName(path)}» supera el límite de {MaxFileSizeMb} MB por archivo.");
                fileHash.AppendData(buffer, 0, read);
            }
            contents.Add(Path.GetFileName(path) + ":" + Convert.ToHexString(fileHash.GetHashAndReset()));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(contents.OrderBy(value => value, StringComparer.Ordinal)))));
    }

    private ClipboardClip? ReadCurrentClipboard()
    {
        if (ForegroundWindowTracker.IsClipboardOwnedByCurrentProcess())
            return null;

        var sourceApp = foregroundWindowTracker.GetForegroundProcessName();
        if (ExcludedApplications.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), sourceApp, StringComparison.OrdinalIgnoreCase))) return null;
        var data = Clipboard.GetDataObject();
        if (data is null || data.GetDataPresent(ExclusionFormat) || data.GetDataPresent(WindowsPasteService.InternalClipboardFormat, false) || HistoryOptOut(data))
            return null;

        var text = ReadString(data, DataFormats.UnicodeText) ?? ReadString(data, DataFormats.Text);
        var html = ReadString(data, DataFormats.Html);
        var rtf = ReadString(data, DataFormats.Rtf);
        var files = data.GetDataPresent(DataFormats.FileDrop)
            ? data.GetData(DataFormats.FileDrop) as string[]
            : null;
        var image = files is not { Length: > 0 } && data.GetDataPresent(DataFormats.Bitmap)
            ? data.GetData(DataFormats.Bitmap) as BitmapSource
            : null;
        var imageBytes = EncodeImage(image);

        if (string.IsNullOrEmpty(text) && imageBytes is null && (files is null || files.Length == 0))
            return null;

        var kind = files is { Length: > 0 }
            ? ClipKind.Files
            : imageBytes is not null ? ClipKind.Image : ClipKind.Text;
        var preview = files is { Length: > 0 }
            ? files.Length == 1 ? Path.GetFileName(files[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : $"{files.Length} archivos y carpetas"
            : !string.IsNullOrWhiteSpace(text)
            ? NormalizePreview(text)
            : imageBytes is not null
                ? "Imagen copiada"
                : "Texto copiado";

        var clip = new ClipboardClip
        {
            Kind = kind,
            PreviewText = preview,
            TextContent = text,
            HtmlContent = html,
            RtfContent = rtf,
            ImageBytes = imageBytes,
            ThumbnailBytes = imageBytes is null ? null : ImageThumbnail.Create(imageBytes),
            FilesJson = files is { Length: > 0 } ? JsonSerializer.Serialize(files) : null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SourceApp = sourceApp
        };

        return clip with { ContentHash = ComputeHash(clip) };
    }

    private static string? ReadString(IDataObject data, string format)
    {
        if (!data.GetDataPresent(format))
            return null;
        var value = data.GetData(format, true);
        return value switch
        {
            string stringValue when !string.IsNullOrEmpty(stringValue) => stringValue,
            MemoryStream stream => Encoding.UTF8.GetString(stream.ToArray()),
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => null
        };
    }

    private static bool HistoryOptOut(IDataObject data)
    {
        const string format = "CanIncludeInClipboardHistory";
        if (!data.GetDataPresent(format, false)) return false;
        var value = data.GetData(format, false);
        var bytes = value switch { MemoryStream stream => stream.ToArray(), byte[] array => array, _ => null };
        return value is false || value is int number && number == 0 || bytes is { Length: >= 4 } && BitConverter.ToInt32(bytes, 0) == 0;
    }

    private static byte[]? EncodeImage(BitmapSource? image)
    {
        if (image is null)
            return null;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string NormalizePreview(string text)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length > 300 ? string.Concat(normalized.AsSpan(0, 297), "…") : normalized;
    }

    private static string ComputeHash(ClipboardClip clip)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(clip.TextContent);
        Append(clip.HtmlContent);
        Append(clip.RtfContent);
        Append(clip.FilesJson);
        if (clip.ImageBytes is not null)
            hash.AppendData(clip.ImageBytes);
        return Convert.ToHexString(hash.GetHashAndReset());

        void Append(string? value)
        {
            if (!string.IsNullOrEmpty(value))
                hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData([0]);
        }
    }
}

public sealed class ClipboardCaptureException(string message) : Exception(message);
