using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Threading;
using ClickBoard.App.Settings;

namespace ClickBoard.App;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private readonly Func<ClickBoardSettings, string?> _saveSettings;
    private ClickBoardSettings _currentSettings;
    private readonly DispatcherTimer _fileLimitTimer;
    private readonly DispatcherTimer _exclusionsTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool _restoring;
    private bool _fileLimitInvalid;
    private string _quickPickerHotkey;
    private string _dockedPanelHotkey;
    private HotkeyTarget _captureTarget;
    private string _currentSection = "General";
    public event EventHandler? CloseRequested;
    public event EventHandler? CleanupRequested;
    private void ReviewCleanup_Click(object sender, RoutedEventArgs e) => CleanupRequested?.Invoke(this, EventArgs.Empty);

    public SettingsView(ClickBoardSettings settings, string hotkeyStatus, Func<ClickBoardSettings, string?> saveSettings)
    {
        InitializeComponent();
        _saveSettings = saveSettings;
        VersionText.Text = "ClipForge " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.2.0");
        _currentSettings = settings with { MaxFileSizeMb = Math.Clamp(settings.MaxFileSizeMb, 1, 10240) };
        _fileLimitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _fileLimitTimer.Tick += (_, _) => { _fileLimitTimer.Stop(); ApplyFileLimit(); };
        _quickPickerHotkey = settings.QuickPickerHotkey;
        _dockedPanelHotkey = settings.DockedPanelHotkey;
        QuickPickerHotkeyButton.Content = _quickPickerHotkey;
        DockedPanelHotkeyButton.Content = _dockedPanelHotkey;
        HotkeyStatusText.Text = hotkeyStatus;
        (settings.Theme switch
        {
            "Light" => LightThemeOption,
            "System" => SystemThemeOption,
            _ => DarkThemeOption
        }).IsChecked = true;
        QuickEnterPlainOption.IsChecked = settings.QuickEnterAction == QuickPasteAction.PlainText;
        QuickEnterPreserveOption.IsChecked = settings.QuickEnterAction == QuickPasteAction.PreserveFormatting;
        EnableAlternatePasteCheck.IsChecked = settings.EnableAlternatePasteAction;
        EnableFavoriteQuickActionCheck.IsChecked = settings.EnableFavoriteQuickAction;
        MaxFileSizeBox.Text = Math.Clamp(settings.MaxFileSizeMb, 1, 10240).ToString();
        ExcludedAppsBox.Text = settings.ExcludedApplications;
        CapturePausedCheck.IsChecked = settings.CapturePaused;
        CloseQuickOnFocusLossCheck.IsChecked = settings.CloseQuickOnFocusLoss;
        (settings.MaxStorageMb switch { 512 => StorageSmallOption, 5120 => StorageLargeOption, _ => StorageStandardOption }).IsChecked = true;
        _exclusionsTimer.Tick += (_, _) => { _exclusionsTimer.Stop(); ApplyExclusions(); };
        ExcludedAppsBox.TextChanged += (_, _) => { _exclusionsTimer.Stop(); _exclusionsTimer.Start(); };
        ExcludedAppsBox.LostKeyboardFocus += (_, _) => { _exclusionsTimer.Stop(); ApplyExclusions(); };
        Loaded += (_, _) => ApplyFilter();
        SettingsSearch.GotKeyboardFocus += (_, _) => CancelCapture();
        QuickPickerHotkeyButton.LostKeyboardFocus += (_, _) => { if (_captureTarget == HotkeyTarget.QuickPicker) CancelCapture(); };
        DockedPanelHotkeyButton.LostKeyboardFocus += (_, _) => { if (_captureTarget == HotkeyTarget.DockedPanel) CancelCapture(); };
        foreach (var option in new System.Windows.Controls.Primitives.ToggleButton[] { DarkThemeOption, LightThemeOption, SystemThemeOption, QuickEnterPlainOption, QuickEnterPreserveOption, StorageSmallOption, StorageStandardOption, StorageLargeOption })
            option.Checked += OptionChanged;
        foreach (var option in new System.Windows.Controls.Primitives.ToggleButton[] { EnableAlternatePasteCheck, EnableFavoriteQuickActionCheck, CapturePausedCheck, CloseQuickOnFocusLossCheck })
        {
            option.Checked += OptionChanged;
            option.Unchecked += OptionChanged;
        }
        MaxFileSizeBox.TextChanged += (_, _) =>
        {
            if (_restoring) return;
            _fileLimitTimer.Stop();
            _fileLimitTimer.Start();
            SaveFeedback.Text = "Aplicando tamaño…";
        };
        MaxFileSizeBox.LostKeyboardFocus += (_, _) => { _fileLimitTimer.Stop(); ApplyFileLimit(); };
        Unloaded += (_, _) => { _fileLimitTimer.Stop(); _exclusionsTimer.Stop(); };
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        if (_restoring) return;
        var updated = sender switch
        {
            var control when ReferenceEquals(control, DarkThemeOption) => _currentSettings with { Theme = "Dark" },
            var control when ReferenceEquals(control, LightThemeOption) => _currentSettings with { Theme = "Light" },
            var control when ReferenceEquals(control, SystemThemeOption) => _currentSettings with { Theme = "System" },
            var control when ReferenceEquals(control, QuickEnterPlainOption) => _currentSettings with { QuickEnterAction = QuickPasteAction.PlainText },
            var control when ReferenceEquals(control, QuickEnterPreserveOption) => _currentSettings with { QuickEnterAction = QuickPasteAction.PreserveFormatting },
            var control when ReferenceEquals(control, EnableAlternatePasteCheck) => _currentSettings with { EnableAlternatePasteAction = EnableAlternatePasteCheck.IsChecked == true },
            var control when ReferenceEquals(control, EnableFavoriteQuickActionCheck) => _currentSettings with { EnableFavoriteQuickAction = EnableFavoriteQuickActionCheck.IsChecked == true },
            var control when ReferenceEquals(control, CapturePausedCheck) => _currentSettings with { CapturePaused = CapturePausedCheck.IsChecked == true },
            var control when ReferenceEquals(control, CloseQuickOnFocusLossCheck) => _currentSettings with { CloseQuickOnFocusLoss = CloseQuickOnFocusLossCheck.IsChecked == true },
            System.Windows.Controls.RadioButton control when control.GroupName == "Storage" => _currentSettings with { MaxStorageMb = int.Parse((string)control.Tag) },
            _ => _currentSettings
        };
        if (ApplySettings(updated)) return;
        _restoring = true;
        try
        {
            ( _currentSettings.Theme switch { "Light" => LightThemeOption, "System" => SystemThemeOption, _ => DarkThemeOption }).IsChecked = true;
            QuickEnterPlainOption.IsChecked = _currentSettings.QuickEnterAction == QuickPasteAction.PlainText;
            QuickEnterPreserveOption.IsChecked = _currentSettings.QuickEnterAction == QuickPasteAction.PreserveFormatting;
            EnableAlternatePasteCheck.IsChecked = _currentSettings.EnableAlternatePasteAction;
            EnableFavoriteQuickActionCheck.IsChecked = _currentSettings.EnableFavoriteQuickAction;
            CapturePausedCheck.IsChecked = _currentSettings.CapturePaused;
            CloseQuickOnFocusLossCheck.IsChecked = _currentSettings.CloseQuickOnFocusLoss;
            (_currentSettings.MaxStorageMb switch { 512 => StorageSmallOption, 5120 => StorageLargeOption, _ => StorageStandardOption }).IsChecked = true;
        }
        finally { _restoring = false; }
    }

    private bool ApplySettings(ClickBoardSettings updated)
    {
        if (updated == _currentSettings) return true;
        var error = _saveSettings(updated);
        if (error is not null)
        {
            ValidationText.Text = error;
            ValidationText.Visibility = Visibility.Visible;
            SaveFeedback.Text = "No se pudo aplicar el cambio; se mantiene la configuración anterior.";
            return false;
        }
        _currentSettings = updated;
        if (_fileLimitInvalid)
        {
            ValidationText.Text = $"Introduce un tamaño entre 1 y 10240 MB. Sigue activo el límite de {_currentSettings.MaxFileSizeMb} MB.";
            ValidationText.Visibility = Visibility.Visible;
        }
        else ValidationText.Visibility = Visibility.Collapsed;
        SaveFeedback.Text = "Cambio aplicado y guardado.";
        return true;
    }

    private bool ApplyFileLimit()
    {
        if (!int.TryParse(MaxFileSizeBox.Text, out var size) || size is < 1 or > 10240)
        {
            _fileLimitInvalid = true;
            ValidationText.Text = $"Introduce un tamaño entre 1 y 10240 MB. Sigue activo el límite de {_currentSettings.MaxFileSizeMb} MB.";
            ValidationText.Visibility = Visibility.Visible;
            SaveFeedback.Text = "El tamaño no se ha cambiado.";
            return false;
        }
        if (_fileLimitInvalid)
        {
            _fileLimitInvalid = false;
            ValidationText.Visibility = Visibility.Collapsed;
            SaveFeedback.Text = "El tamaño indicado es válido.";
        }
        if (size == _currentSettings.MaxFileSizeMb)
        {
            if (SaveFeedback.Text == "Aplicando tamaño…") SaveFeedback.Text = "Configuración al día.";
            return true;
        }
        return ApplySettings(_currentSettings with { MaxFileSizeMb = size });
    }

    private void CloseView()
    {
        _exclusionsTimer.Stop();
        if (!ApplyExclusions()) { SelectSection("Privacidad"); ExcludedAppsBox.Focus(); return; }
        _fileLimitTimer.Stop();
        if (!ApplyFileLimit())
        {
            SelectSection("Archivos");
            MaxFileSizeBox.Focus();
            return;
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool ApplyExclusions() => ApplySettings(_currentSettings with { ExcludedApplications = ExcludedAppsBox.Text.Trim() });

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string section }) return;
        SelectSection(section);
    }

    private void SelectSection(string section)
    {
        CancelCapture();
        _currentSection = section;
        SettingsSearch.Clear();
        ApplyFilter();
        ContentScroll.ScrollToTop();
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SettingsSearch.Text.Trim();
        var searching = query.Length > 0;
        var sections = new[] { ("General", GeneralSection, GeneralNav), ("Atajos", ShortcutsSection, ShortcutsNav), ("Pegado", PasteSection, PasteNav), ("Archivos", FilesSection, FilesNav), ("Privacidad", PrivacySection, PrivacyNav) };
        var visible = 0;
        foreach (var (name, section, button) in sections)
        {
            var matches = searching
                ? CultureInfo.CurrentCulture.CompareInfo.IndexOf((string)section.Tag, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                : name == _currentSection;
            section.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) visible++;
            if (!searching && name == _currentSection)
            {
                button.SetResourceReference(BackgroundProperty, "AccentSoftBrush");
                button.SetResourceReference(ForegroundProperty, "AccentBrush");
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.ClearValue(BackgroundProperty);
                button.SetResourceReference(ForegroundProperty, "TextBrush");
                button.FontWeight = FontWeights.Normal;
            }
        }
        NoResultsText.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = searching ? "Resultados de búsqueda" : _currentSection switch { "Atajos" => "Atajos globales", "Pegado" => "Pegado rápido", _ => _currentSection };
        PageDescription.Text = searching ? $"Opciones relacionadas con «{query}»." : _currentSection switch
        {
            "General" => "Personaliza la apariencia de ClipForge.",
            "Atajos" => "Abre ClipForge desde cualquier aplicación.",
            "Pegado" => "Decide cómo se comportan las acciones rápidas.",
            "Privacidad" => "Controla cuándo y desde dónde se guarda contenido.",
            _ => "Controla los archivos que se guardan en tu historial."
        };
    }

    private void CancelCapture()
    {
        if (_captureTarget == HotkeyTarget.None) return;
        _captureTarget = HotkeyTarget.None;
        QuickPickerHotkeyButton.Content = _quickPickerHotkey;
        DockedPanelHotkeyButton.Content = _dockedPanelHotkey;
    }

    private void CaptureQuickPickerHotkey_Click(object sender, RoutedEventArgs e) => BeginCapture(HotkeyTarget.QuickPicker);
    private void CaptureDockedPanelHotkey_Click(object sender, RoutedEventArgs e) => BeginCapture(HotkeyTarget.DockedPanel);

    private void BeginCapture(HotkeyTarget target)
    {
        _captureTarget = target;
        ValidationText.Visibility = Visibility.Collapsed;
        var button = target == HotkeyTarget.QuickPicker ? QuickPickerHotkeyButton : DockedPanelHotkeyButton;
        button.Content = "Pulsa la combinación…";
        button.Focus();
    }

    private void View_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_captureTarget == HotkeyTarget.None && e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SettingsSearch.Focus();
            SettingsSearch.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _captureTarget == HotkeyTarget.None)
        {
            if (SettingsSearch.IsKeyboardFocusWithin && !string.IsNullOrEmpty(SettingsSearch.Text)) SettingsSearch.Clear();
            else CloseView();
            e.Handled = true;
            return;
        }

        if (_captureTarget == HotkeyTarget.None)
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            _captureTarget = HotkeyTarget.None;
            QuickPickerHotkeyButton.Content = _quickPickerHotkey;
            DockedPanelHotkeyButton.Content = _dockedPanelHotkey;
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            ValidationText.Text = "Incluye Ctrl, Alt o Shift en el atajo.";
            ValidationText.Visibility = Visibility.Visible;
            e.Handled = true;
            return;
        }

        var hotkey = FormatHotkey(modifiers, key);
        var previousQuick = _quickPickerHotkey;
        var previousDocked = _dockedPanelHotkey;
        if (_captureTarget == HotkeyTarget.QuickPicker)
        {
            _quickPickerHotkey = hotkey;
            QuickPickerHotkeyButton.Content = hotkey;
        }
        else
        {
            _dockedPanelHotkey = hotkey;
            DockedPanelHotkeyButton.Content = hotkey;
        }

        _captureTarget = HotkeyTarget.None;
        if (!ApplySettings(_currentSettings with { QuickPickerHotkey = _quickPickerHotkey, DockedPanelHotkey = _dockedPanelHotkey }))
        {
            _quickPickerHotkey = previousQuick;
            _dockedPanelHotkey = previousDocked;
            QuickPickerHotkeyButton.Content = previousQuick;
            DockedPanelHotkeyButton.Content = previousDocked;
            HotkeyStatusText.Text = "No se pudo actualizar el atajo. Revisa el estado indicado abajo.";
        }
        else HotkeyStatusText.Text = $"Selector: {_quickPickerHotkey} · Panel: {_dockedPanelHotkey}";
        e.Handled = true;
    }

    private static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            Key.Space => "Space",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            _ => key.ToString()
        });
        return string.Join('+', parts);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => CloseView();

    private enum HotkeyTarget
    {
        None,
        QuickPicker,
        DockedPanel
    }
}
