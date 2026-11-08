using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using ClickBoard.App.ViewModels;
using ClickBoard.App.Settings;
using ClickBoard.Core.Services;
using ClickBoard.Core.Models;
using ClickBoard.Infrastructure.Storage;
using ClickBoard.Infrastructure.Windows;
using ClickBoard.App.Network;

namespace ClickBoard.App;

public partial class App : System.Windows.Application
{
    private IClipboardRepository? _repository;
    private ForegroundWindowTracker? _foregroundTracker;
    private WindowsMessageHost? _messageHost;
    private ClipboardCaptureService? _captureService;
    private WindowsPasteService? _pasteService;
    private ClipboardViewModel? _mainViewModel;
    private ClipboardViewModel? _quickViewModel;
    private ClipboardViewModel? _dockedViewModel;
    private ClickBoardSettings _settings = new();
    private MainWindow? _mainWindow;
    private QuickPasteWindow? _quickWindow;
    private DockedQuickWindow? _dockedWindow;
    private SettingsView? _settingsView;
    private NetworkView? _networkView;
    private LanTransferService? _network;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private bool _shuttingDown;
    private CancellationTokenSource? _captureCancellation;
    private CancellationTokenSource _captureShutdown = new();
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsInstanceMutex;
    private bool _cleanupRunning;
    private long[] _pasteQueue = [];
    private int _queuePosition;
    private int? _queueLast;
    private bool _queueBusy;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var userId = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _instanceMutex = new Mutex(true, $@"Local\ClipForge-{userId}", out _ownsInstanceMutex);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\ClipForge-Activate-{userId}");
        if (!_ownsInstanceMutex)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        try
        {
            _settings = ClickBoardSettingsStore.Load();
            StorageQuota.MaximumMb = Math.Clamp(_settings.MaxStorageMb, 512, 102400);
            ThemeManager.Apply(_settings.Theme);
            _repository = new SqliteClipboardRepository();
            await _repository.InitializeAsync();
            _foregroundTracker = new ForegroundWindowTracker();
            _pasteService = new WindowsPasteService(_foregroundTracker);
            _captureService = new ClipboardCaptureService(_repository, _foregroundTracker)
            {
                MaxFileSizeMb = Math.Clamp(_settings.MaxFileSizeMb, 1, 10240),
                ExcludedApplications = _settings.ExcludedApplications
            };
            _messageHost = new WindowsMessageHost(_settings.QuickPickerHotkey, _settings.DockedPanelHotkey);
            _messageHost.ClipboardChanged += OnClipboardChanged;
            _messageHost.QuickModeRequested += OnQuickModeRequested;
            _messageHost.QueueActionRequested += async action => await HandleQueueAsync(action);

            _mainViewModel = CreateViewModel();
            _quickViewModel = CreateViewModel(false);
            _dockedViewModel = CreateViewModel(false);
            _mainWindow = new MainWindow(_mainViewModel);
            _mainWindow.TrashRetentionDays = _settings.TrashRetentionDays;
            _mainWindow.SaveTrashRetention = days => SaveSettings(_settings with { TrashRetentionDays = days });
            _mainWindow.SetCapturePaused(_settings.CapturePaused);
            _mainWindow.QuickPickerRequested += (_, _) => ShowQuickPicker();
            _mainWindow.DockedPanelRequested += (_, _) => ShowDockedPanel();
            _mainWindow.SettingsRequested += (_, _) => ShowSettings();
            _mainWindow.NetworkRequested += (_, _) => ShowNetwork();
            _mainWindow.ProjectChanged += async project =>
            {
                try
                {
                    if (ProjectDetection.Current.Enabled) ProjectDetection.Save(ProjectDetection.Current with { Enabled = false });
                    if (_quickViewModel is not null) await _quickViewModel.SelectProjectAsync(project);
                    if (_dockedViewModel is not null) await _dockedViewModel.SelectProjectAsync(project);
                }
                catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException)
                { _mainViewModel.StatusMessage = "No se pudo actualizar el proyecto en las vistas rápidas."; }
            };
            _mainWindow.QueueRequested += clips =>
            {
                if (_queueBusy) { _mainViewModel.StatusMessage = "Espera a que termine el pegado de la cola."; return; }
                if (clips.Count == 0) { _messageHost.DisableQueue(); _pasteQueue = []; _queueLast = null; _mainViewModel.StatusMessage = "Cola vacía."; return; }
                if (!_messageHost.TryEnableQueue(out var error)) { _pasteQueue = []; _mainViewModel.StatusMessage = error; return; }
                _pasteQueue = clips.Select(clip => clip.Id).ToArray(); _queuePosition = 0; _queueLast = null;
                _mainViewModel.StatusMessage = $"Cola: {_pasteQueue.Length} clips. Ctrl+Alt+Mayús: V pegar, N saltar, B retroceder, R repetir.";
            };
            _mainWindow.ClipTitleChanged += async (_, _) =>
            {
                try { await RefreshOpenViewsAsync(); }
                catch (Exception) { _mainViewModel.StatusMessage = "No se pudo actualizar el historial."; }
            };
            _mainWindow.ClipDeleted += async (_, _) =>
            {
                try
                {
                    if (_quickWindow?.IsVisible == true && _quickViewModel is not null) await _quickViewModel.RefreshAsync();
                    if (_dockedWindow?.IsVisible == true && _dockedViewModel is not null) await _dockedViewModel.RefreshAsync();
                }
                catch (Exception) { _mainViewModel.StatusMessage = "Clip eliminado; no se pudieron actualizar todas las vistas."; }
            };
            _mainWindow.Show();
            _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent,
                (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)), null, Timeout.Infinite, executeOnlyOnce: false);

            CreateTrayIcon();
            InitializeProjectDetection();
            InitializeTrashRetention();
            InitializeFavoriteShortcuts();
            InitializeNetwork();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferencesChanged;
            await _mainViewModel.RefreshAsync();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show($"ClipForge no pudo iniciar.\n\n{exception.Message}", "ClipForge", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private ClipboardViewModel CreateViewModel(bool loadSelectionPreview = true) => new(_repository!, _pasteService!, loadSelectionPreview);

    private void OnSystemPreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme == "System") Dispatcher.BeginInvoke(new Action(() => ThemeManager.Apply("System")));
    }

    private async void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (_captureService is null || _settings.CapturePaused)
            return;

        _captureCancellation?.Cancel();
        _captureCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _captureCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            await Task.Delay(250, token);
            var saved = await _captureService.TryCaptureAsync(_captureShutdown.Token);
            if (saved)
                await RefreshOpenViewsAsync();
        }
        catch (OperationCanceledException) { }
        catch (ClipboardCaptureException exception)
        {
            if (_mainViewModel is not null) _mainViewModel.StatusMessage = exception.Message;
            if (_trayIcon is not null)
                _trayIcon.ShowBalloonTip(5000, "ClipForge", exception.Message, System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (StorageQuotaException exception)
        {
            if (_mainViewModel is not null) _mainViewModel.StatusMessage = exception.Message;
            _trayIcon?.ShowBalloonTip(5000, "Almacenamiento de ClipForge", exception.Message, System.Windows.Forms.ToolTipIcon.Warning);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_mainViewModel is not null)
                _mainViewModel.StatusMessage = "No se pudo guardar este elemento del portapapeles";
        }
    }

    private async Task RefreshOpenViewsAsync()
    {
        if (_mainViewModel is not null)
            await _mainViewModel.RefreshAsync();
        if (_quickWindow?.IsVisible == true && _quickViewModel is not null)
            await _quickViewModel.RefreshAsync();
        if (_dockedWindow?.IsVisible == true && _dockedViewModel is not null)
            await _dockedViewModel.RefreshAsync();
    }

    private void OnQuickModeRequested(object? sender, QuickModeEventArgs e)
    {
        if (e.Mode == QuickMode.KeyboardPicker)
            ShowQuickPicker();
        else
            ShowDockedPanel();
    }

    private void ShowQuickPicker()
    {
        if (_quickWindow is null)
        {
            _quickWindow = new QuickPasteWindow(_quickViewModel!, _settings);
            _quickWindow.Closed += (_, _) => _quickWindow = null;
        }
        _quickViewModel!.SearchText = string.Empty;
        _ = _quickViewModel.SetSectionAsync("Historial");
        _quickWindow.Show();
        _quickWindow.Activate();
        _quickWindow.FocusSearch();
    }

    private void ShowDockedPanel()
    {
        if (_dockedWindow is null)
        {
            _dockedWindow = new DockedQuickWindow(_dockedViewModel!, _settings);
            _dockedWindow.Closed += (_, _) => _dockedWindow = null;
        }
        _dockedViewModel!.SearchText = string.Empty;
        _ = _dockedViewModel.SetKindFilterAsync(null);
        _dockedWindow.PlaceAtScreenEdge();
        _dockedWindow.Show();
        _dockedWindow.Activate();
        _dockedWindow.FocusSearch();
    }

    private void CreateTrayIcon()
    {
        var iconResource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ClipForge.ico"));
        if (iconResource is null) throw new InvalidOperationException("No se encontró el icono de ClipForge.");
        using (iconResource.Stream)
        using (var icon = new Icon(iconResource.Stream))
            _trayIconImage = (Icon)icon.Clone();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir ClipForge", null, (_, _) => ShowMainWindow());
        menu.Items.Add($"Selector rápido  ·  {_settings.QuickPickerHotkey}", null, (_, _) => ShowQuickPicker());
        menu.Items.Add($"Panel lateral  ·  {_settings.DockedPanelHotkey}", null, (_, _) => ShowDockedPanel());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApplication());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "ClipForge · historial local del portapapeles",
            Icon = _trayIconImage,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowSettings()
    {
        if (_mainWindow is null || _settingsView is not null)
            return;

        _settingsView = new SettingsView(_settings, _messageHost?.HotkeyStatus ?? string.Empty, SaveSettings);
        _settingsView.CleanupRequested += async (_, _) => await ReviewCleanupAsync();
        _settingsView.CloseRequested += (_, _) =>
        {
            _mainWindow.ClosePage();
            _settingsView = null;
        };
        _mainWindow.ShowPage(_settingsView);
    }

    private async Task HandleQueueAsync(int action)
    {
        if (_queueBusy || _pasteQueue.Length == 0 || _repository is null || _pasteService is null) return;
        _queueBusy = true;
        try
        {
            if (action == 1) { _queuePosition = Math.Min(_pasteQueue.Length, _queuePosition + 1); }
            else if (action == 2) { _queuePosition = Math.Max(0, _queuePosition - 1); }
            else
            {
                var index = action == 3 ? _queueLast : _queuePosition;
                if (index is null || index >= _pasteQueue.Length)
                { QueueStatus("No hay un clip para pegar. Usa B para retroceder o R para repetir el último pegado."); return; }
                var target = _pasteService.CaptureTargetWindow();
                var clip = await _repository.GetByIdAsync(_pasteQueue[index.Value]);
                if (clip is null) { QueueStatus("El clip ya no está disponible. Usa N para saltarlo."); return; }
                if (!await _pasteService.PasteAsync(clip, targetWindow: target))
                { QueueStatus("No se pudo enviar el pegado. La posición se conserva; el clip está copiado."); return; }
                _queueLast = index;
                if (action != 3) _queuePosition++;
            }
            QueueStatus(_queuePosition == _pasteQueue.Length ? "Cola terminada. R repite el último; B retrocede." : $"Cola: siguiente {_queuePosition + 1} de {_pasteQueue.Length}. V pega; N salta; B retrocede; R repite (Ctrl+Alt+Mayús).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException or Microsoft.Data.Sqlite.SqliteException or JsonException)
        { QueueStatus("No se pudo pegar el clip. La posición de la cola se conserva."); }
        finally { _queueBusy = false; }
    }

    private void QueueStatus(string message)
    {
        if (_mainViewModel is not null) _mainViewModel.StatusMessage = message;
        _trayIcon?.ShowBalloonTip(2500, "Cola de pegado", message, System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ShowNetwork()
    {
        if (_mainWindow is null || _network is null || _networkView is not null) return;
        _networkView = new NetworkView(_network);
        _networkView.CloseRequested += (_, _) => { _mainWindow.ClosePage(); _networkView = null; };
        _mainWindow.ShowPage(_networkView);
    }

    private static HashSet<string> GetActiveClipboardFiles()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (System.Windows.Clipboard.ContainsFileDropList())
            foreach (string? path in System.Windows.Clipboard.GetFileDropList())
                if (!string.IsNullOrEmpty(path)) paths.Add(Path.GetFullPath(path));
        return paths;
    }

    private static bool UsesActiveFiles(ClipboardClip clip, HashSet<string> active)
    {
        if (clip.FilesJson is null) return false;
        try
        {
            return (JsonSerializer.Deserialize<string[]>(clip.FilesJson) ?? []).Any(path => active.Contains(Path.GetFullPath(path)));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or IOException) { return true; }
    }

    private async Task ReviewCleanupAsync()
    {
        if (_cleanupRunning || _repository is null || _mainWindow is null) return;
        _cleanupRunning = true;
        var disabled = false;
        try
        {
            var before = DateTimeOffset.UtcNow.AddDays(-30);
            var active = GetActiveClipboardFiles();
            var candidates = (await _repository.GetCleanupCandidatesAsync(before)).Where(clip => !UsesActiveFiles(clip, active)).ToArray();
            if (candidates.Length == 0)
            { System.Windows.MessageBox.Show(_mainWindow, "No hay clips que cumplan las condiciones de limpieza.", "Limpieza guiada"); return; }
            var preview = string.Join("\n", candidates.Take(8).Select(clip => "• " + clip.ShortPreview));
            var answer = System.Windows.MessageBox.Show(_mainWindow,
                $"Se eliminarán hasta {candidates.Length} clips de más de 30 días, sin título, colección ni etiquetas y no favoritos.\n\n{preview}\n\nLas copias locales identificadas como propias también se borrarán. Los archivos originales no se modifican. ¿Continuar?",
                "Revisar limpieza", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;
            _mainWindow.IsEnabled = false;
            if (_quickWindow is not null) _quickWindow.IsEnabled = false;
            if (_dockedWindow is not null) _dockedWindow.IsEnabled = false;
            disabled = true;
            var deleted = 0;
            var retainedCopies = 0;
            foreach (var clip in candidates)
            {
                if (UsesActiveFiles(clip, GetActiveClipboardFiles())) continue;
                if (!await _repository.DeleteIfUnprotectedAsync(clip.Id, before)) continue;
                deleted++;
                if (UsesActiveFiles(clip, GetActiveClipboardFiles())) { retainedCopies++; continue; }
                if (!await Task.Run(() => OwnedFileStore.DeleteCopies(clip.FilesJson))) retainedCopies++;
            }
            await RefreshOpenViewsAsync();
            _mainViewModel!.StatusMessage = $"Limpieza: {deleted} clips eliminados; {retainedCopies} copias conservadas por seguridad o por un error de acceso.";
            System.Windows.MessageBox.Show(_mainWindow, _mainViewModel.StatusMessage, "Limpieza completada");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or Microsoft.Data.Sqlite.SqliteException)
        { System.Windows.MessageBox.Show(_mainWindow, "No se pudo completar la limpieza. El portapapeles o el almacenamiento no están disponibles.", "Limpieza guiada"); }
        finally
        {
            if (disabled)
            {
                _mainWindow.IsEnabled = true;
                if (_quickWindow is not null) _quickWindow.IsEnabled = true;
                if (_dockedWindow is not null) _dockedWindow.IsEnabled = true;
            }
            _cleanupRunning = false;
        }
    }

    private void InitializeNetwork()
    {
        _network = new LanTransferService(() => _settings.MaxFileSizeMb);
        if (_mainWindow is not null) _mainWindow.TransferService = _network;
        _network.RequestApproval = offer => Dispatcher.InvokeAsync(() =>
        {
            _trayIcon?.ShowBalloonTip(5000, "Solicitud de archivo", $"{offer.DeviceName} quiere enviarte {offer.FileName}.", System.Windows.Forms.ToolTipIcon.Info);
            _mainWindow?.ShowFromTray();
            var text = $"{offer.DeviceName} quiere enviarte un archivo.\n\nArchivo: {offer.FileName}\nTamaño: {offer.Length / 1024.0 / 1024.0:F2} MB\nIdentidad: {offer.Fingerprint}\n\n¿Aceptar y guardar el archivo?";
            return System.Windows.MessageBox.Show(_mainWindow, text, offer.IsContact ? "Archivo de un contacto" : "Archivo de un dispositivo desconocido", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }).Task;
        _network.FileReceived += receipt => Dispatcher.BeginInvoke(new Action(() =>
        {
            _trayIcon?.ShowBalloonTip(6000, "Archivo recibido", $"{receipt.DeviceName} te envió {receipt.FileName}.", System.Windows.Forms.ToolTipIcon.Info);
            if (_mainViewModel is not null) _mainViewModel.StatusMessage = $"Archivo recibido de {receipt.DeviceName}: {receipt.FileName}";
        }));
        if (_network.WasEnabled)
        {
            try { _network.Start(_network.DisplayName); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException)
            {
                _trayIcon?.ShowBalloonTip(6000, "Red no disponible", "No se pudo activar el modo red. Abre Red y contactos para intentarlo de nuevo.", System.Windows.Forms.ToolTipIcon.Warning);
            }
        }
    }

    private string? SaveSettings(ClickBoardSettings settings)
    {
        var previous = _settings;
        var hotkeysChanged = previous.QuickPickerHotkey != settings.QuickPickerHotkey ||
            previous.DockedPanelHotkey != settings.DockedPanelHotkey;
        if (hotkeysChanged && _messageHost is not null && !_messageHost.TrySetHotkeys(
                settings.QuickPickerHotkey,
                settings.DockedPanelHotkey,
                out var hotkeyError))
            return $"{hotkeyError} {_messageHost.HotkeyStatus}";

        try
        {
            ClickBoardSettingsStore.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (hotkeysChanged && _messageHost is not null)
                _messageHost.TrySetHotkeys(previous.QuickPickerHotkey, previous.DockedPanelHotkey, out _);
            return "No se pudieron guardar las preferencias. Inténtalo otra vez.";
        }

        _settings = settings;
        if (_mainWindow is not null) _mainWindow.TrashRetentionDays = settings.TrashRetentionDays;
        _mainWindow?.SetCapturePaused(settings.CapturePaused);
        if (settings.CapturePaused && !previous.CapturePaused)
        {
            _captureCancellation?.Cancel();
            _captureShutdown.Cancel();
        }
        else if (!settings.CapturePaused && previous.CapturePaused)
        {
            _captureShutdown.Dispose();
            _captureShutdown = new CancellationTokenSource();
        }
        StorageQuota.MaximumMb = Math.Clamp(settings.MaxStorageMb, 512, 102400);
        if (_captureService is not null)
        {
            _captureService.MaxFileSizeMb = settings.MaxFileSizeMb;
            _captureService.ExcludedApplications = settings.ExcludedApplications;
        }
        ThemeManager.Apply(settings.Theme);
        _quickWindow?.UpdateSettings(settings);
        _dockedWindow?.UpdateSettings(settings);
        if (_trayIcon?.ContextMenuStrip?.Items.Count > 2)
        {
            _trayIcon.ContextMenuStrip.Items[1].Text = $"Selector rápido  ·  {settings.QuickPickerHotkey}";
            _trayIcon.ContextMenuStrip.Items[2].Text = $"Panel lateral  ·  {settings.DockedPanelHotkey}";
        }
        return null;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
            return;
        _mainWindow.ShowFromTray();
        _ = _mainViewModel?.RefreshAsync();
    }

    private void ExitApplication()
    {
        if (_shuttingDown)
            return;
        _shuttingDown = true;
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();
        _captureCancellation?.Cancel();
        _captureCancellation?.Dispose();
        _captureShutdown.Cancel();
        _network?.Dispose();
        _messageHost?.Dispose();
        _foregroundTracker?.Dispose();
        _mainViewModel?.Dispose();
        _quickViewModel?.Dispose();
        _dockedViewModel?.Dispose();
        _mainWindow?.CloseForExit();
        _quickWindow?.Close();
        _dockedWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _projectTimer?.Stop();
        _trashTimer?.Stop();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferencesChanged;
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        if (!_shuttingDown)
        {
            _trayIcon?.Dispose();
            _trayIconImage?.Dispose();
            _captureCancellation?.Cancel();
            _captureCancellation?.Dispose();
            _captureShutdown.Cancel();
            _network?.Dispose();
            _messageHost?.Dispose();
            _foregroundTracker?.Dispose();
        }
        base.OnExit(e);
    }
}
