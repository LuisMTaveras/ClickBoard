using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace ClickBoard.App;

public partial class App
{
    private DispatcherTimer? _trashTimer;
    private bool _purgingTrash;
    private void InitializeTrashRetention()
    {
        _trashTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _trashTimer.Tick += async (_, _) =>
        {
            var days = _settings.TrashRetentionDays;
            if (_purgingTrash || _shuttingDown || days is not (7 or 30 or 90) || _mainViewModel is null) return;
            _purgingTrash = true;
            try
            {
                var before = DateTimeOffset.UtcNow.AddDays(-days);
                var expired = await _mainViewModel.GetExpiredTrashAsync(before);
                var completed = 0; var skipped = 0;
                foreach (var clip in expired)
                {
                    // A setting changed while waiting cancels this cleanup batch.
                    if (_shuttingDown || _settings.TrashRetentionDays != days) break;
                    try { if (await _mainViewModel.PermanentlyDeleteAsync(clip, before)) completed++; else skipped++; }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException)
                    { skipped++; }
                }
                if (completed > 0 || skipped > 0) _mainViewModel.StatusMessage = $"Papelera: {completed} clips vencidos borrados; {skipped} omitidos o con copias conservadas por seguridad.";
            }
            catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
            { _mainViewModel.StatusMessage = "No se pudo completar la revisión automática de la papelera."; }
            finally { _purgingTrash = false; }
        };
        _trashTimer.Start();
    }
}
