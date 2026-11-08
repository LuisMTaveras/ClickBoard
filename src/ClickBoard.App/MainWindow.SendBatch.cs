using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ClickBoard.App.Network;
using ClickBoard.Core.Models;
using ClickBoard.Infrastructure.Storage;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace ClickBoard.App;

public partial class MainWindow
{
    internal LanTransferService? TransferService { get; set; }
    private bool _batchSending;
    private void SendBatch_Click(object sender, RoutedEventArgs e) => OpenSendBatch();

    internal void OpenSendBatch(LanPeer? initialPeer = null, bool autoStart = false)
    {
        var service = TransferService;
        if (_batchSending) return;
        if (service?.IsRunning != true) { _viewModel.StatusMessage = "Activa Red y contactos antes de enviar."; return; }
        var selected = ClipList.Items.OfType<ClipboardClip>().Where(clip => ClipList.SelectedItems.Contains(clip)).ToArray();
        if (selected.Length == 0) { _viewModel.StatusMessage = "Selecciona los clips que quieras enviar."; return; }
        var (rootLayout, _, panel) = CreateSubpageLayout(
            "Red local",
            "Enviar selección por red",
            $"{selected.Length} clips seleccionados. Los textos se envían como TXT y las imágenes como PNG.");
        var peers = CreateThemedListBox(height: 200);
        var availablePeers = service.GetPeers();
        peers.ItemsSource = availablePeers;
        peers.DisplayMemberPath = "Name";
        if (initialPeer is not null)
            peers.SelectedItem = availablePeers.FirstOrDefault(p => p.Fingerprint == initialPeer.Fingerprint) ?? initialPeer;
        panel.Children.Add(peers);
        var status = new TextBlock { Text = "Elige un dispositivo disponible.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 14) };
        status.SetResourceReference(ForegroundProperty, "MutedBrush");
        panel.Children.Add(status);
        var send = new Button { Content = "Enviar lote", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        var refresh = new Button { Content = "Actualizar dispositivos", Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancelar envío", IsEnabled = false, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        var back = new Button { Content = "←  Volver", Style = (Style)FindResource("SecondaryButton") };
        CancellationTokenSource? cancellation = null;
        refresh.Click += (_, _) => peers.ItemsSource = service.GetPeers();
        cancel.Click += (_, _) => cancellation?.Cancel(); back.Click += (_, _) => ClosePage();
        send.Click += async (_, _) =>
        {
            if (_batchSending || peers.SelectedItem is not LanPeer peer) return;
            _batchSending = true; send.IsEnabled = back.IsEnabled = refresh.IsEnabled = peers.IsEnabled = false; cancel.IsEnabled = true;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token; var completed = 0;
            try
            {
                foreach (var item in selected)
                {
                    token.ThrowIfCancellationRequested();
                    var clip = await _viewModel.LoadClipAsync(item.Id) ?? throw new IOException("Un clip ya no está disponible.");
                    if (clip.Kind == ClipKind.Files)
                    {
                        var paths = JsonSerializer.Deserialize<string[]>(clip.FilesJson ?? "[]") ?? [];
                        if (paths.Length == 0) throw new IOException("No hay archivos para enviar.");
                        foreach (var path in paths)
                        { status.Text = $"{completed} archivos enviados. Enviando {Path.GetFileName(path)}…"; await service.SendFileAsync(peer, path, token); completed++; }
                    }
                    else
                    {
                        var payload = clip.Kind == ClipKind.Image ? clip.ImageBytes : clip.TextContent is { } text ? new UTF8Encoding(false).GetBytes(text) : null;
                        if (payload is null) throw new IOException("Contenido no disponible.");
                        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "outgoing", Guid.NewGuid().ToString("N"));
                        var file = Path.Combine(directory, $"ClipForge-{clip.Id}" + (clip.Kind == ClipKind.Image ? ".png" : ".txt"));
                        try
                        {
                            using (await StorageQuota.AcquireAsync(payload.LongLength, token))
                            { Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(file, payload, token); }
                            status.Text = $"{completed} archivos enviados. Enviando {Path.GetFileName(file)}…";
                            await service.SendFileAsync(peer, file, token); completed++;
                        }
                        finally
                        {
                            if (File.Exists(file)) File.Delete(file);
                            if (Directory.Exists(directory)) Directory.Delete(directory);
                        }
                    }
                }
                status.Text = $"Lote completado: {completed} archivos enviados.";
            }
            catch (OperationCanceledException) { status.Text = $"Lote cancelado: {completed} archivos confirmados. Los ya recibidos se conservan."; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or Microsoft.Data.Sqlite.SqliteException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException)
            { status.Text = $"Lote detenido: {completed} archivos confirmados. Puedes reintentar para reanudar pendientes sin duplicar los ya confirmados."; }
            finally
            { cancellation.Dispose(); cancellation = null; _batchSending = false; send.IsEnabled = back.IsEnabled = refresh.IsEnabled = peers.IsEnabled = true; cancel.IsEnabled = false; }
        };
        var bar = new WrapPanel();
        bar.Children.Add(send);
        bar.Children.Add(refresh);
        bar.Children.Add(cancel);
        bar.Children.Add(back);
        panel.Children.Add(bar);
        ShowPage(rootLayout);
        if (autoStart && peers.SelectedItem is LanPeer)
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
