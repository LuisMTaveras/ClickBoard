using System.IO;

namespace ClickBoard.Infrastructure.Storage;

public static class StorageQuota
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static long _reservedBytes;
    private static long _cachedUsedBytes;
    private static long _cachedAtTicks;

    public static int MaximumMb { get; set; } = 2048;

    public static void InvalidateCache() => Interlocked.Exchange(ref _cachedAtTicks, 0);

    public static async Task<IDisposable> AcquireAsync(long additionalBytes, CancellationToken token)
    {
        if (additionalBytes < 0)
            throw new StorageQuotaException($"Se alcanzó el presupuesto de almacenamiento ({MaximumMb} MB). Elimina clips o aumenta el límite en Configuración.");

        await Gate.WaitAsync(token);
        try
        {
            var budgetBytes = (long)MaximumMb * 1024 * 1024;
            var reserved = Interlocked.Read(ref _reservedBytes);
            var now = Environment.TickCount64;
            var cachedAt = Interlocked.Read(ref _cachedAtTicks);
            var used = cachedAt != 0 && now - cachedAt < 3000
                ? Interlocked.Read(ref _cachedUsedBytes)
                : await RefreshUsedBytesAsync(token);

            // Re-measure from disk if cached estimate is close to the budget limit.
            if (additionalBytes > budgetBytes - used - reserved && cachedAt != 0 && now - cachedAt < 3000)
                used = await RefreshUsedBytesAsync(token);

            if (additionalBytes > budgetBytes - used - reserved)
                throw new StorageQuotaException($"Se alcanzó el presupuesto de almacenamiento ({MaximumMb} MB). Elimina clips o aumenta el límite en Configuración.");

            Interlocked.Add(ref _reservedBytes, additionalBytes);
            return new Lease(additionalBytes);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<long> RefreshUsedBytesAsync(CancellationToken token)
    {
        var used = await Task.Run(() => GetUsedBytes(token), token);
        Interlocked.Exchange(ref _cachedUsedBytes, used);
        Interlocked.Exchange(ref _cachedAtTicks, Environment.TickCount64);
        return used;
    }

    public static int CleanExportCache(ISet<string> activePaths)
    {
        var exportsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "exports");
        if (!Directory.Exists(exportsDir)) return 0;

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(exportsDir))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (activePaths.Contains(fullPath)) continue;
                File.Delete(fullPath);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        if (deleted > 0)
            InvalidateCache();
        return deleted;
    }

    private static long GetUsedBytes(CancellationToken token)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard");
        if (!Directory.Exists(root)) return 0;
        long used = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
        {
            token.ThrowIfCancellationRequested();
            try { used = checked(used + new FileInfo(path).Length); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return used;
    }

    private sealed class Lease(long reservedBytes) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Add(ref _reservedBytes, -reservedBytes);
                Interlocked.Add(ref _cachedUsedBytes, reservedBytes);
            }
        }
    }
}

public sealed class StorageQuotaException(string message) : IOException(message);
