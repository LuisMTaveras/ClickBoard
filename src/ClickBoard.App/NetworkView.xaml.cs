using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClickBoard.App.Network;

namespace ClickBoard.App;

public partial class NetworkView : System.Windows.Controls.UserControl
{
    private readonly LanTransferService _network;
    private readonly ObservableCollection<PeerRow> _rows = [];
    private readonly ObservableCollection<LanTransferUpdate> _transferRows = [];
    private bool _refreshing;
    private string? _selectedFingerprint;
    private bool _sending;
    private bool _updatingAlias;
    private string? _pendingAliasFingerprint;
    private readonly DispatcherTimer _deviceNameTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _aliasTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    public event EventHandler? CloseRequested;

    public NetworkView(LanTransferService network)
    {
        InitializeComponent();
        _network = network;
        DeviceNameBox.Text = network.DisplayName;
        PeersList.ItemsSource = _rows;
        TransfersList.ItemsSource = _transferRows;
        _deviceNameTimer.Tick += (_, _) => { _deviceNameTimer.Stop(); ApplyDeviceName(); };
        _aliasTimer.Tick += (_, _) => { _aliasTimer.Stop(); ApplyContactAlias(); };
        DeviceNameBox.TextChanged += (_, _) =>
        {
            _deviceNameTimer.Stop();
            _deviceNameTimer.Start();
            ActionStatusText.Text = "Aplicando nombre del dispositivo…";
        };
        DeviceNameBox.LostKeyboardFocus += (_, _) => { _deviceNameTimer.Stop(); ApplyDeviceName(); };
        AliasBox.TextChanged += (_, _) =>
        {
            if (_updatingAlias || _refreshing || _selectedFingerprint is null || _network.GetContactName(_selectedFingerprint) is null) return;
            _pendingAliasFingerprint = _selectedFingerprint;
            _aliasTimer.Stop();
            _aliasTimer.Start();
            ActionStatusText.Text = "Aplicando nombre del contacto…";
        };
        AliasBox.LostKeyboardFocus += (_, _) => { _aliasTimer.Stop(); ApplyContactAlias(); };
        ContactAutoReceiveCheck.Checked += AutoReceiveChanged;
        ContactAutoReceiveCheck.Unchecked += AutoReceiveChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _network.Changed += NetworkChanged;
        _network.StatusChanged += NetworkStatusChanged;
        _network.TransferChanged += TransferChanged;
        _network.FileReceived += InboxReceived;
        RefreshInbox();
        _transferRows.Clear();
        foreach (var transfer in _network.GetTransfers().OrderByDescending(t => t.UpdatedAt)) _transferRows.Add(transfer);
        RefreshRows();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _network.Changed -= NetworkChanged;
        _network.StatusChanged -= NetworkStatusChanged;
        _network.TransferChanged -= TransferChanged;
        _network.FileReceived -= InboxReceived;
        _deviceNameTimer.Stop();
        _aliasTimer.Stop();
    }

    private void NetworkChanged() => Dispatcher.BeginInvoke(new Action(RefreshRows));
    private void NetworkStatusChanged(string message) => Dispatcher.BeginInvoke(new Action(() => ActionStatusText.Text = message));
    private void TransferChanged(LanTransferUpdate update) => Dispatcher.BeginInvoke(new Action(() =>
    {
        var old = _transferRows.FirstOrDefault(t => t.Id == update.Id);
        if (old is not null) _transferRows.Remove(old);
        _transferRows.Insert(0, update);
        while (_transferRows.Count > 100) _transferRows.RemoveAt(_transferRows.Count - 1);
    }));
    private void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id }) _network.CancelTransfer(id);
    }

    private void RefreshRows()
    {
        var selected = (PeersList.SelectedItem as PeerRow)?.Fingerprint ?? _selectedFingerprint;
        var contacts = _network.GetContacts();
        var peers = _network.GetPeers();
        var rows = new List<PeerRow>();
        foreach (var contact in contacts)
        {
            var peer = peers.FirstOrDefault(p => string.Equals(p.Fingerprint, contact.Fingerprint, StringComparison.OrdinalIgnoreCase));
            rows.Add(new PeerRow(contact.Fingerprint, contact.Alias, peer, true));
        }
        foreach (var peer in peers.Where(p => contacts.All(c => !string.Equals(c.Fingerprint, p.Fingerprint, StringComparison.OrdinalIgnoreCase))))
            rows.Add(new PeerRow(peer.Fingerprint, peer.Name, peer, false));
        _refreshing = true;
        _rows.Clear();
        foreach (var row in rows.OrderByDescending(r => r.IsContact).ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)) _rows.Add(row);
        PeersList.SelectedItem = _rows.FirstOrDefault(r => string.Equals(r.Fingerprint, selected, StringComparison.OrdinalIgnoreCase));
        _refreshing = false;
        UpdateSelection();
        EmptyPeersText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPeersText.Text = _network.IsRunning ? "No se han encontrado otros dispositivos. Activa ClipForge en ambos equipos." : "Activa la red en ambos equipos para descubrir dispositivos.";
        ToggleNetworkButton.Content = _network.IsRunning ? "Desactivar red" : "Activar red";
        NetworkStatusText.Text = _network.IsRunning ? "Activo · visible en la red local" : "Red desactivada.";
        IdentityText.Text = _network.Fingerprint;
    }

    private void PeersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var row = PeersList.SelectedItem as PeerRow;
        if (!string.Equals(_selectedFingerprint, row?.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            _aliasTimer.Stop();
            if (!ApplyContactAlias()) _pendingAliasFingerprint = null;
            _selectedFingerprint = row?.Fingerprint;
            _updatingAlias = true;
            try { AliasBox.Text = row?.DisplayName ?? string.Empty; }
            finally { _updatingAlias = false; }
        }
        AliasBox.IsEnabled = row is not null;
        AddContactButton.Visibility = row?.IsContact == true ? Visibility.Collapsed : Visibility.Visible;
        AddContactButton.IsEnabled = row is { IsContact: false, Peer: not null };
        RemoveContactButton.IsEnabled = row?.IsContact == true;
        _updatingAlias = true;
        try
        {
            ContactAutoReceiveCheck.IsEnabled = row?.IsContact == true;
            ContactAutoReceiveCheck.IsChecked = row?.IsContact == true && _network.GetContactAutoReceive(row.Fingerprint);
        }
        finally { _updatingAlias = false; }
        SendFileButton.IsEnabled = !_sending && _network.IsRunning && row?.Peer is not null;
    }

    private void AutoReceiveChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingAlias || PeersList.SelectedItem is not PeerRow { IsContact: true } row) return;
        try { _network.SetContactAutoReceive(row.Fingerprint, ContactAutoReceiveCheck.IsChecked == true); ActionStatusText.Text = "Permiso de recepción actualizado."; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ActionStatusText.Text = "No se pudo cambiar el permiso del contacto.";
            _updatingAlias = true;
            try { ContactAutoReceiveCheck.IsChecked = _network.GetContactAutoReceive(row.Fingerprint); }
            finally { _updatingAlias = false; }
        }
    }

    private void ToggleNetwork_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_network.IsRunning) _network.Stop();
            else
            {
                _deviceNameTimer.Stop();
                if (!ApplyDeviceName()) return;
                _network.Start(_network.DisplayName);
            }
            RefreshRows();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Sockets.SocketException or System.Security.Cryptography.CryptographicException)
        {
            ActionStatusText.Text = "No se pudo cambiar el estado de la red: " + exception.Message;
        }
    }

    private bool ApplyDeviceName()
    {
        var name = DeviceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ActionStatusText.Text = $"Escribe un nombre. Se mantiene «{_network.DisplayName}».";
            return false;
        }
        if (name == _network.DisplayName)
        {
            if (ActionStatusText.Text == "Aplicando nombre del dispositivo…") ActionStatusText.Text = "Nombre del dispositivo actualizado.";
            return true;
        }
        try { _network.SetDisplayName(name); ActionStatusText.Text = "Nombre del dispositivo actualizado."; return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ActionStatusText.Text = "No se pudo guardar el nombre; se mantiene el anterior.";
            return false;
        }
    }

    private bool ApplyContactAlias()
    {
        if (_pendingAliasFingerprint is not { } fingerprint) return true;
        var previous = _network.GetContactName(fingerprint);
        if (previous is null) { _pendingAliasFingerprint = null; return true; }
        var alias = AliasBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(alias))
        {
            ActionStatusText.Text = $"Escribe un nombre. Se mantiene «{previous}».";
            return false;
        }
        if (alias == previous) { _pendingAliasFingerprint = null; return true; }
        try
        {
            _network.AddContact(fingerprint, alias);
            _pendingAliasFingerprint = null;
            ActionStatusText.Text = "Nombre del contacto actualizado.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ActionStatusText.Text = "No se pudo guardar el nombre del contacto; se mantiene el anterior.";
            return false;
        }
    }

    private void AddContact_Click(object sender, RoutedEventArgs e)
    {
        if (PeersList.SelectedItem is not PeerRow row) return;
        if (row.IsContact || row.Peer is null) return;
        var answer = System.Windows.MessageBox.Show(Window.GetWindow(this),
            $"Antes de confiar en este dispositivo, compara este código completo con el que aparece en su pantalla de Red y contactos:\n\n{row.Fingerprint}\n\n¿Coinciden los códigos? Los archivos de este contacto se guardarán automáticamente.",
            "Verificar contacto", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            _network.AddContact(row.Fingerprint, string.IsNullOrWhiteSpace(AliasBox.Text) ? row.DisplayName : AliasBox.Text);
            _selectedFingerprint = row.Fingerprint;
            ActionStatusText.Text = "Contacto guardado. El otro equipo también debe agregarte para recibir automáticamente.";
            RefreshRows();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) { ActionStatusText.Text = "No se pudo guardar el contacto."; }
    }

    private void RemoveContact_Click(object sender, RoutedEventArgs e)
    {
        if (PeersList.SelectedItem is not PeerRow { IsContact: true } row) return;
        var answer = System.Windows.MessageBox.Show(Window.GetWindow(this), $"¿Quitar a «{row.DisplayName}» de tus contactos? Sus próximos archivos requerirán aceptación.", "Quitar contacto", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _aliasTimer.Stop();
        _pendingAliasFingerprint = null;
        try { _network.RemoveContact(row.Fingerprint); ActionStatusText.Text = "Contacto eliminado."; RefreshRows(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { ActionStatusText.Text = "No se pudo quitar el contacto."; }
    }

    private async void SendFile_Click(object sender, RoutedEventArgs e)
    {
        if (_sending || PeersList.SelectedItem is not PeerRow { Peer: { } peer }) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Elegir archivo para enviar", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        _sending = true;
        SendFileButton.IsEnabled = false;
        ActionStatusText.Text = $"Conectando con {peer.Name} y esperando su respuesta…";
        try { await _network.SendFileAsync(peer, dialog.FileName); }
        catch (OperationCanceledException) { ActionStatusText.Text = "El envío se canceló o agotó el tiempo de espera."; }
        catch (Exception exception)
        { ActionStatusText.Text = "No se pudo enviar el archivo: " + exception.Message; }
        finally { _sending = false; UpdateSelection(); }
    }

    private void OpenReceived_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(_network.ReceivedDirectory); Process.Start(new ProcessStartInfo(_network.ReceivedDirectory) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { ActionStatusText.Text = "No se pudo abrir la carpeta de archivos recibidos."; }
    }
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _deviceNameTimer.Stop();
        _aliasTimer.Stop();
        if (!ApplyDeviceName()) { DeviceNameBox.Focus(); return; }
        if (!ApplyContactAlias()) { AliasBox.Focus(); return; }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public sealed record PeerRow(string Fingerprint, string DisplayName, LanPeer? Peer, bool IsContact)
    {
        public string Badge => IsContact ? "CONTACTO" : "DISPOSITIVO";
        public string Description => Peer is null ? "Sin conexión" : $"Disponible · {Peer.Address}";
    }
}
