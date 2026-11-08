using System.Diagnostics;
using System.IO;
using System.Windows;
using ClickBoard.App.Network;

namespace ClickBoard.App;

public partial class NetworkView
{
    private void InboxReceived(LanReceipt receipt) => Dispatcher.BeginInvoke(new Action(RefreshInbox));

    private void RefreshInbox()
    {
        try
        {
            InboxList.ItemsSource = LanInbox.Load();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ActionStatusText.Text = "No se pudo leer la bandeja de recibidos.";
        }
    }

    private void RefreshInbox_Click(object sender, RoutedEventArgs e) => RefreshInbox();

    private void OpenInboxFolder_Click(object sender, RoutedEventArgs e)
    {
        if (InboxList.SelectedItem is not LanReceipt receipt)
            return;
        try
        {
            if (!LanInbox.IsReceivedPath(receipt.SavedPath))
                return;
            var directory = Path.GetDirectoryName(receipt.SavedPath)!;
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException();
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            ActionStatusText.Text = "La carpeta recibida ya no está disponible.";
        }
    }

    private async void ResendInbox_Click(object sender, RoutedEventArgs e)
    {
        if (_sending)
            return;
        if (InboxList.SelectedItem is not LanReceipt receipt || PeersList.SelectedItem is not PeerRow { Peer: { } peer })
        {
            ActionStatusText.Text = "Selecciona un recibido y un dispositivo disponible.";
            return;
        }
        _sending = true;
        SendFileButton.IsEnabled = ResendInboxButton.IsEnabled = false;
        try
        {
            if (!LanInbox.IsReceivedPath(receipt.SavedPath) || !File.Exists(receipt.SavedPath))
                throw new FileNotFoundException();
            ActionStatusText.Text = $"Reenviando {receipt.FileName} a {peer.Name}…";
            await _network.SendFileAsync(peer, receipt.SavedPath);
            ActionStatusText.Text = "Archivo reenviado.";
        }
        catch (OperationCanceledException)
        {
            ActionStatusText.Text = "Reenvío cancelado o sin actividad.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException)
        {
            ActionStatusText.Text = "No se pudo reenviar. Comprueba el archivo y el dispositivo.";
        }
        finally
        {
            _sending = false;
            ResendInboxButton.IsEnabled = true;
            UpdateSelection();
        }
    }
}
