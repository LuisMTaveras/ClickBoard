using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ClickBoard.App.Settings;

namespace ClickBoard.App;

public partial class App
{
    private DispatcherTimer? _projectTimer;
    private bool _detectingProject;

    private void InitializeProjectDetection()
    {
        try
        {
            ProjectDetection.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            if (_mainViewModel is not null)
                _mainViewModel.StatusMessage = "No se pudieron cargar las reglas automáticas de proyecto.";
        }

        _projectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _projectTimer.Tick += async (_, _) =>
        {
            if (_detectingProject || _shuttingDown || !ProjectDetection.Current.Enabled || _foregroundTracker is null || _mainViewModel is null)
                return;
            var context = _foregroundTracker.GetActiveContext();
            if (context is not { } active)
                return;

            var matches = ProjectDetection.Match(active.Process, active.Title);
            if (matches.Length != 1)
            {
                _mainViewModel.StatusMessage = matches.Length == 0
                    ? "Proyecto automático: ninguna regla coincide; se conserva el proyecto actual."
                    : "Proyecto automático: varias coincidencias; selecciona el proyecto manualmente.";
                return;
            }
            if (string.Equals(_mainViewModel.ActiveProject, matches[0], StringComparison.OrdinalIgnoreCase))
                return;

            _detectingProject = true;
            try
            {
                await _mainViewModel.SelectProjectAsync(matches[0]);
                if (!ProjectDetection.Current.Enabled || !string.Equals(_mainViewModel.ActiveProject, matches[0], StringComparison.OrdinalIgnoreCase))
                    return;
                if (_quickViewModel is not null)
                    await _quickViewModel.SelectProjectAsync(matches[0]);
                if (!ProjectDetection.Current.Enabled || !string.Equals(_mainViewModel.ActiveProject, matches[0], StringComparison.OrdinalIgnoreCase))
                    return;
                if (_dockedViewModel is not null)
                    await _dockedViewModel.SelectProjectAsync(matches[0]);
                _mainViewModel.StatusMessage = $"Proyecto automático: {matches[0]} · {active.Process}.";
            }
            catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
            {
                _mainViewModel.StatusMessage = "No se pudo activar el proyecto detectado.";
            }
            finally
            {
                _detectingProject = false;
            }
        };
        _projectTimer.Start();
    }
}
