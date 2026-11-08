using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Buffers.Binary;
using ClickBoard.Infrastructure.Storage;

namespace ClickBoard.App.Network;

public sealed record LanContact(string Fingerprint, string Alias)
{
    public bool ReceiveAutomatically { get; init; } = true;
}
public sealed record LanPeer(string Fingerprint, string Name, string Address, DateTimeOffset LastSeen);
public sealed record LanOffer(string Fingerprint, string DeviceName, string FileName, long Length, bool IsContact);
public sealed record LanReceipt(string DeviceName, string FileName, string SavedPath)
{
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Description => $"{FileName} · {DeviceName} · {ReceivedAt.ToLocalTime():g}";
}
public sealed record LanTransferUpdate(string Id, string DeviceName, string FileName, long Completed, long Total, bool Receiving, string State)
{
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public double BytesPerSecond { get; init; }
    public string? PeerFingerprint { get; init; }
    public string? SourcePath { get; init; }
    public double Percent => Total == 0 ? (State == "Completado" ? 100 : 0) : Math.Clamp(100.0 * Completed / Total, 0, 100);
    public string Description => $"{(Receiving ? "Recibiendo de" : "Enviando a")} {DeviceName} · {State} · {Completed / 1048576.0:F1}/{Total / 1048576.0:F1} MB" + (State == "En curso" ? $" · {BytesPerSecond / 1048576.0:F1} MB/s" : string.Empty);
    public bool Active => State is "Conectando" or "Esperando aceptación" or "En curso";
    public bool CanRetry => !Receiving && !Active && State != "Completado" && !string.IsNullOrEmpty(SourcePath) && !string.IsNullOrEmpty(PeerFingerprint);
}
public sealed class LanSettings
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = Environment.MachineName;
    public List<LanContact> Contacts { get; set; } = [];
}

public sealed class LanTransferService : IDisposable
{
    private const int Port = 49573;
    private const int MaxFrame = 16384;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "network");
    private readonly string _receivedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard", "Received");
    private readonly Func<int> _maxFileSizeMb;
    private readonly object _settingsLock = new();
    private readonly ConcurrentDictionary<string, LanPeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _incomingSlots = new(4, 4);
    private readonly LanSettings _settings;
    private X509Certificate2? _certificate;
    private UdpClient? _udp;
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private string? _fingerprint;
    private readonly ConcurrentDictionary<string, LanTransferUpdate> _transfers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _transferCancellation = new();
    private readonly ConcurrentDictionary<string, string> _completedTransfers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _outgoingSlots = new(2, 2);

    public event Action? Changed;
    public event Action<string>? StatusChanged;
    public event Action<LanReceipt>? FileReceived;
    public event Action<LanTransferUpdate>? TransferChanged;
    public IReadOnlyList<LanTransferUpdate> GetTransfers() => _transfers.Values.ToArray();
    public void CancelTransfer(string id) { if (_transferCancellation.TryGetValue(id, out var source)) { try { source.Cancel(); } catch (ObjectDisposedException) { } } }
    private void ReportTransfer(LanTransferUpdate update)
    {
        if (_transfers.TryGetValue(update.Id, out var previous) && update.State == "En curso")
        {
            var seconds = (DateTimeOffset.UtcNow - previous.UpdatedAt).TotalSeconds;
            if (seconds > 0) update = update with { BytesPerSecond = Math.Max(0, update.Completed - previous.Completed) / seconds };
        }
        update = update with { UpdatedAt = DateTimeOffset.UtcNow };
        if (_transfers.Count >= 100 && !_transfers.ContainsKey(update.Id))
            foreach (var item in _transfers.Values.Where(t => !t.Active).Take(20)) _transfers.TryRemove(item.Id, out _);
        _transfers[update.Id] = update;
        TransferChanged?.Invoke(update);
    }
    public Func<LanOffer, Task<bool>>? RequestApproval { get; set; }
    public bool IsRunning => _shutdown is not null;
    public bool WasEnabled { get { lock (_settingsLock) return _settings.Enabled; } }
    public string DisplayName { get { lock (_settingsLock) return _settings.DisplayName; } }
    public string Fingerprint => _fingerprint ?? "Se mostrará al activar la red";
    public string ReceivedDirectory => _receivedDirectory;

    public LanTransferService(Func<int> maxFileSizeMb)
    {
        _maxFileSizeMb = maxFileSizeMb;
        _settings = LoadSettings();
        _settings.Contacts ??= [];
        _settings.DisplayName = CleanName(_settings.DisplayName);
    }

    public IReadOnlyList<LanPeer> GetPeers() => _peers.Values.Where(p => DateTimeOffset.UtcNow - p.LastSeen < TimeSpan.FromSeconds(18)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    public IReadOnlyList<LanContact> GetContacts() { lock (_settingsLock) return _settings.Contacts.ToArray(); }
    public string? GetContactName(string fingerprint) { lock (_settingsLock) return _settings.Contacts.FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))?.Alias; }
    public bool GetContactAutoReceive(string fingerprint) { lock (_settingsLock) return _settings.Contacts.FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))?.ReceiveAutomatically == true; }
    public void SetContactAutoReceive(string fingerprint, bool enabled)
    {
        lock (_settingsLock)
        {
            var index = _settings.Contacts.FindIndex(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var previous = _settings.Contacts[index];
            _settings.Contacts[index] = previous with { ReceiveAutomatically = enabled };
            try { SaveSettings(); } catch { _settings.Contacts[index] = previous; throw; }
        }
        Changed?.Invoke();
    }

    public void AddContact(string fingerprint, string alias)
    {
        if (!ValidFingerprint(fingerprint)) throw new ArgumentException("La identidad del dispositivo no es válida.");
        alias = CleanName(alias);
        lock (_settingsLock)
        {
            var previous = _settings.Contacts.ToList();
            var autoReceive = _settings.Contacts.FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))?.ReceiveAutomatically ?? true;
            _settings.Contacts.RemoveAll(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            _settings.Contacts.Add(new LanContact(fingerprint.ToUpperInvariant(), alias) { ReceiveAutomatically = autoReceive });
            try { SaveSettings(); }
            catch { _settings.Contacts = previous; throw; }
        }
        Changed?.Invoke();
    }

    public void RemoveContact(string fingerprint)
    {
        lock (_settingsLock)
        {
            var previous = _settings.Contacts.ToList();
            _settings.Contacts.RemoveAll(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
            try { SaveSettings(); }
            catch { _settings.Contacts = previous; throw; }
        }
        Changed?.Invoke();
    }

    public void SetDisplayName(string name)
    {
        lock (_settingsLock)
        {
            var previous = _settings.DisplayName;
            _settings.DisplayName = CleanName(name);
            try { SaveSettings(); }
            catch { _settings.DisplayName = previous; throw; }
        }
        Changed?.Invoke();
    }

    public void Start(string displayName)
    {
        if (IsRunning) return;
        var name = CleanName(displayName);
        EnsureIdentity();
        var udp = new UdpClient(AddressFamily.InterNetwork);
        TcpListener? listener = null;
        try
        {
            udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            var shutdown = new CancellationTokenSource();
            _udp = udp;
            _listener = listener;
            _shutdown = shutdown;
            lock (_settingsLock) { _settings.DisplayName = name; _settings.Enabled = true; SaveSettings(); }
            _ = ReceiveAnnouncementsAsync(udp, shutdown.Token);
            _ = BroadcastAsync(udp, shutdown.Token);
            _ = AcceptConnectionsAsync(listener, shutdown.Token);
            StatusChanged?.Invoke("Red activada. Buscando dispositivos cercanos…");
            Changed?.Invoke();
        }
        catch
        {
            _shutdown?.Cancel();
            _shutdown?.Dispose();
            _shutdown = null;
            _udp = null;
            _listener = null;
            udp.Dispose();
            listener?.Stop();
            throw;
        }
    }

    public void Stop()
    {
        StopCore();
        lock (_settingsLock) { _settings.Enabled = false; SaveSettings(); }
        StatusChanged?.Invoke("Red desactivada.");
        Changed?.Invoke();
    }

    private void StopCore()
    {
        var shutdown = _shutdown;
        _shutdown = null;
        shutdown?.Cancel();
        _udp?.Dispose();
        _listener?.Stop();
        _udp = null;
        _listener = null;
        _peers.Clear();
        shutdown?.Dispose();
    }

    public async Task RetryTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        if (!_transfers.TryGetValue(transferId, out var existing) || !existing.CanRetry)
            throw new InvalidOperationException("Esta transferencia no se puede reintentar.");
        var peer = GetPeers().FirstOrDefault(p => string.Equals(p.Fingerprint, existing.PeerFingerprint, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("El dispositivo de destino no está disponible en la red en este momento.");
        await SendFileAsync(peer, existing.SourcePath!, cancellationToken, existing.Id);
    }

    public async Task SendFileAsync(LanPeer peer, string path, CancellationToken cancellationToken = default, string? existingId = null)
    {
        if (!_outgoingSlots.Wait(0)) throw new InvalidOperationException("Ya hay dos envíos en curso. Espera a que terminen o cancela uno.");
        var id = existingId ?? Guid.NewGuid().ToString("N");
        var previousCompleted = _transfers.TryGetValue(id, out var prev) ? prev.Completed : 0;
        var previousTotal = prev?.Total ?? 0;
        var update = new LanTransferUpdate(id, GetContactName(peer.Fingerprint) ?? peer.Name, Path.GetFileName(path), previousCompleted, previousTotal, false, "Conectando")
        {
            PeerFingerprint = peer.Fingerprint,
            SourcePath = path
        };
        try
        {
            ReportTransfer(update);
            await SendFileCoreAsync(peer, path, id, cancellationToken);
            if (_transfers.TryGetValue(id, out var final)) ReportTransfer(final with { Completed = final.Total, State = "Completado" });
        }
        catch (OperationCanceledException)
        {
            if (_transfers.TryGetValue(id, out var final)) ReportTransfer(final with { State = "Cancelado / sin actividad" });
            throw;
        }
        catch
        {
            if (_transfers.TryGetValue(id, out var final)) ReportTransfer(final with { State = "Falló" });
            throw;
        }
        finally { _transferCancellation.TryRemove(id, out _); _outgoingSlots.Release(); }
    }

    private async Task SendFileCoreAsync(LanPeer peer, string path, string id, CancellationToken cancellationToken)
    {
        if (!IsRunning || _certificate is null) throw new InvalidOperationException("Activa el modo red antes de enviar archivos.");
        if (!IPAddress.TryParse(peer.Address, out var address) || !IsPrivateAddress(address) || !ValidFingerprint(peer.Fingerprint))
            throw new InvalidOperationException("El dispositivo ya no tiene una dirección local válida.");
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("No se encontró el archivo seleccionado.");
        var max = (long)Math.Clamp(_maxFileSizeMb(), 1, 10240) * 1024 * 1024;
        if (file.Length > max) throw new InvalidOperationException($"El archivo supera el límite de {_maxFileSizeMb()} MB.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown!.Token);
        _transferCancellation[id] = timeout;
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var length = input.Length;
        if (length > max) throw new InvalidOperationException($"El archivo supera el límite de {_maxFileSizeMb()} MB.");
        var fileHashBytes = await SHA256.HashDataAsync(input, timeout.Token);
        input.Position = 0;
        var transferId = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{file.Name}:{length}:{Convert.ToHexString(fileHashBytes)}")));

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(address, Port, timeout.Token);
        using var ssl = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null && FingerprintOf(certificate) == peer.Fingerprint.ToUpperInvariant());
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "ClipForge",
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificates = new X509CertificateCollection { _certificate },
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, timeout.Token);
        if (!ssl.IsEncrypted || !ssl.IsMutuallyAuthenticated || ssl.RemoteCertificate is null)
            throw new AuthenticationException("No se pudo autenticar a ambos dispositivos.");
        await WriteFrameAsync(ssl, new TransferHeader(DisplayName, file.Name, length, transferId), timeout.Token);
        ReportTransfer(new LanTransferUpdate(id, GetContactName(peer.Fingerprint) ?? peer.Name, file.Name, 0, length, false, "Esperando aceptación")
        {
            PeerFingerprint = peer.Fingerprint,
            SourcePath = path
        });
        var response = await ReadFrameAsync<TransferReply>(ssl, timeout.Token);
        if (!response.Accepted) throw new InvalidOperationException(response.Message ?? "El destinatario rechazó el archivo.");
        if (response.AlreadyCompleted)
        {
            ReportTransfer(new LanTransferUpdate(id, GetContactName(peer.Fingerprint) ?? peer.Name, file.Name, length, length, false, "Completado")
            {
                PeerFingerprint = peer.Fingerprint,
                SourcePath = path
            });
            StatusChanged?.Invoke($"Archivo ya confirmado en {GetContactName(peer.Fingerprint) ?? peer.Name}: {file.Name}");
            return;
        }
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var lastReport = Environment.TickCount64;
        var buffer = new byte[81920];
        long sent = Math.Clamp(response.ResumeOffset, 0, length);
        if (sent > 0)
            input.Position = sent;
        while (sent < length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - sent)), timeout.Token);
            if (read == 0) throw new IOException("El archivo cambió durante el envío.");
            await ssl.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            sent += read;
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            if (Environment.TickCount64 - lastReport >= 200 || sent == length)
            {
                ReportTransfer(new LanTransferUpdate(id, GetContactName(peer.Fingerprint) ?? peer.Name, file.Name, sent, length, false, "En curso")
                {
                    PeerFingerprint = peer.Fingerprint,
                    SourcePath = path
                });
                lastReport = Environment.TickCount64;
            }
        }
        if (input.Length != length) throw new IOException("El archivo cambió durante el envío.");
        await ssl.WriteAsync(fileHashBytes, timeout.Token);
        await ssl.FlushAsync(timeout.Token);
        var final = await ReadFrameAsync<TransferReply>(ssl, timeout.Token);
        if (!final.Accepted) throw new IOException(final.Message ?? "El destinatario no confirmó la recepción.");
        StatusChanged?.Invoke($"Archivo enviado a {GetContactName(peer.Fingerprint) ?? peer.Name}: {file.Name}");
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsPrivateAddress(remote.Address) || !_incomingSlots.Wait(0)) { client.Dispose(); continue; }
                _ = HandleIncomingAsync(client, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (Exception) { StatusChanged?.Invoke("No se pudo continuar escuchando conexiones de red."); }
    }

    private async Task HandleIncomingAsync(TcpClient client, CancellationToken token)
    {
        string? temporaryFile = null;
        var hasValidTransferId = false;
        var deletePartialOnExit = true;
        var id = Guid.NewGuid().ToString("N");
        try
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var ssl = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null))
            {
                _transferCancellation[id] = timeout;
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, timeout.Token);
                if (!ssl.IsEncrypted || !ssl.IsMutuallyAuthenticated || ssl.RemoteCertificate is null)
                    throw new AuthenticationException("No se pudo autenticar a ambos dispositivos.");
                var fingerprint = FingerprintOf(ssl.RemoteCertificate);
                var header = await ReadFrameAsync<TransferHeader>(ssl, timeout.Token);
                var name = CleanName(header.DeviceName);
                var fileName = SafeFileName(header.FileName);
                var limit = (long)Math.Clamp(_maxFileSizeMb(), 1, 10240) * 1024 * 1024;
                if (fileName is null || header.Length < 0 || header.Length > limit)
                {
                    await WriteFrameAsync(ssl, new TransferReply(false, "Nombre de archivo inválido o tamaño superior al límite."), timeout.Token);
                    return;
                }
                var alias = GetContactName(fingerprint);
                var offer = new LanOffer(fingerprint, alias ?? name, fileName, header.Length, alias is not null);
                hasValidTransferId = ValidFingerprint(header.TransferId);
                var transferKey = hasValidTransferId ? $"{fingerprint}:{header.TransferId!.ToUpperInvariant()}" : null;
                if (transferKey is not null && _completedTransfers.TryGetValue(transferKey, out var existingSavedPath) && File.Exists(existingSavedPath))
                {
                    ReportTransfer(new LanTransferUpdate(id, offer.DeviceName, Path.GetFileName(existingSavedPath), header.Length, header.Length, true, "Completado"));
                    await WriteFrameAsync(ssl, new TransferReply(true, null, header.Length, AlreadyCompleted: true), timeout.Token);
                    return;
                }
                ReportTransfer(new LanTransferUpdate(id, offer.DeviceName, fileName, 0, header.Length, true, "Esperando aceptación"));
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                var accepted = GetContactAutoReceive(fingerprint) || (RequestApproval is not null && await RequestApproval(offer).WaitAsync(timeout.Token));
                if (!accepted)
                {
                    ReportTransfer(new LanTransferUpdate(id, offer.DeviceName, fileName, 0, header.Length, true, "Rechazado"));
                    await WriteFrameAsync(ssl, new TransferReply(false, "El destinatario rechazó el archivo."), timeout.Token);
                    return;
                }
                Directory.CreateDirectory(_receivedDirectory);
                temporaryFile = hasValidTransferId
                    ? Path.Combine(_receivedDirectory, $".{fingerprint[..16]}-{header.TransferId![..32]}.partial")
                    : Path.Combine(_receivedDirectory, $".{Guid.NewGuid():N}.partial");
                long resumeOffset = 0;
                if (hasValidTransferId && File.Exists(temporaryFile))
                {
                    var partialLength = new FileInfo(temporaryFile).Length;
                    if (partialLength > 0 && partialLength < header.Length)
                        resumeOffset = partialLength;
                    else
                        File.Delete(temporaryFile);
                }
                IDisposable quota;
                try { quota = await StorageQuota.AcquireAsync(Math.Max(0, header.Length - resumeOffset) + 65536, timeout.Token); }
                catch (StorageQuotaException exception)
                {
                    ReportTransfer(new LanTransferUpdate(id, offer.DeviceName, fileName, resumeOffset, header.Length, true, "Sin espacio disponible"));
                    await WriteFrameAsync(ssl, new TransferReply(false, exception.Message), timeout.Token);
                    return;
                }
                using var quotaLease = quota;
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                deletePartialOnExit = !hasValidTransferId;
                await using (var output = new FileStream(temporaryFile, resumeOffset > 0 ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    if (resumeOffset > 0)
                    {
                        long hashed = 0;
                        while (hashed < resumeOffset)
                        {
                            var readExisting = await output.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, resumeOffset - hashed)), timeout.Token);
                            if (readExisting == 0) break;
                            hash.AppendData(buffer, 0, readExisting);
                            hashed += readExisting;
                        }
                        output.Position = hashed;
                        resumeOffset = hashed;
                    }
                    await WriteFrameAsync(ssl, new TransferReply(true, null, ResumeOffset: resumeOffset), timeout.Token);
                    long received = resumeOffset;
                    var lastReport = Environment.TickCount64;
                    while (received < header.Length)
                    {
                        var read = await ssl.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, header.Length - received)), timeout.Token);
                        if (read == 0) throw new EndOfStreamException();
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                        received += read;
                        timeout.CancelAfter(TimeSpan.FromSeconds(45));
                        if (Environment.TickCount64 - lastReport >= 200 || received == header.Length)
                        {
                            ReportTransfer(new LanTransferUpdate(id, offer.DeviceName, fileName, received, header.Length, true, "En curso"));
                            lastReport = Environment.TickCount64;
                        }
                    }
                    var expectedHash = new byte[32];
                    await ssl.ReadExactlyAsync(expectedHash, timeout.Token);
                    if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), expectedHash))
                    {
                        deletePartialOnExit = true;
                        throw new IOException("La verificación del archivo falló.");
                    }
                    await output.FlushAsync(timeout.Token);
                }
                var savedPath = MoveWithoutOverwrite(temporaryFile, fileName);
                temporaryFile = null;
                if (transferKey is not null)
                    _completedTransfers[transferKey] = savedPath;
                ReportTransfer(new LanTransferUpdate(id, offer.DeviceName, Path.GetFileName(savedPath), header.Length, header.Length, true, "Completado"));
                var receipt = new LanReceipt(offer.DeviceName, Path.GetFileName(savedPath), savedPath);
                try { LanInbox.Store(receipt); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { StatusChanged?.Invoke("Archivo recibido; no se pudo guardar su entrada en la bandeja."); }
                FileReceived?.Invoke(receipt);
                await WriteFrameAsync(ssl, new TransferReply(true, null), timeout.Token);
            }
        }
        catch (OperationCanceledException)
        { if (_transfers.TryGetValue(id, out var update) && update.State != "Completado") ReportTransfer(update with { State = "Cancelado / sin actividad" }); }
        catch (Exception)
        {
            if (_transfers.TryGetValue(id, out var update) && update.State != "Completado") ReportTransfer(update with { State = "Falló" });
            StatusChanged?.Invoke("No se pudo completar una recepción de archivos.");
        }
        finally
        {
            _transferCancellation.TryRemove(id, out _);
            if (temporaryFile is not null && deletePartialOnExit) { try { File.Delete(temporaryFile); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            _incomingSlots.Release();
        }
    }

    private string MoveWithoutOverwrite(string temporaryFile, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 0; index < 10000; index++)
        {
            var candidate = Path.Combine(_receivedDirectory, index == 0 ? fileName : $"{stem} ({index}){extension}");
            try { File.Move(temporaryFile, candidate, overwrite: false); return candidate; }
            catch (IOException) when (File.Exists(candidate)) { }
        }
        throw new IOException("No se pudo elegir un nombre disponible.");
    }

    private async Task ReceiveAnnouncementsAsync(UdpClient udp, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(token);
                if (packet.Buffer.Length > 2048 || !IsPrivateAddress(packet.RemoteEndPoint.Address)) continue;
                try
                {
                    var announcement = JsonSerializer.Deserialize<Announcement>(packet.Buffer, JsonOptions);
                    if (announcement is null || announcement.Protocol != "ClipForge-LAN-1" || !ValidFingerprint(announcement.Fingerprint) || announcement.Fingerprint.Equals(_fingerprint, StringComparison.OrdinalIgnoreCase)) continue;
                    var key = announcement.Fingerprint.ToUpperInvariant();
                    if (_peers.Count >= 200 && !_peers.ContainsKey(key)) continue;
                    _peers[key] = new LanPeer(key, CleanName(announcement.Name), packet.RemoteEndPoint.Address.ToString(), DateTimeOffset.UtcNow);
                    Changed?.Invoke();
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (Exception) { StatusChanged?.Invoke("No se pudo continuar buscando dispositivos en la red."); }
    }

    private async Task BroadcastAsync(UdpClient udp, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Announcement("ClipForge-LAN-1", DisplayName, _fingerprint!), JsonOptions);
                var targets = GetBroadcastAddresses();
                var anySent = false;
                foreach (var target in targets)
                {
                    try
                    {
                        await udp.SendAsync(bytes, new IPEndPoint(target, Port), token);
                        anySent = true;
                    }
                    catch (SocketException)
                    {
                    }
                }
                if (!anySent)
                    StatusChanged?.Invoke("No se pudo anunciar el dispositivo en la red local.");
                foreach (var pair in _peers.Where(p => DateTimeOffset.UtcNow - p.Value.LastSeen > TimeSpan.FromSeconds(18)))
                    _peers.TryRemove(pair.Key, out _);
                Changed?.Invoke();
                await Task.Delay(TimeSpan.FromSeconds(4), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static IReadOnlyList<IPAddress> GetBroadcastAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || !IsPrivateAddress(unicast.Address))
                        continue;
                    var mask = unicast.IPv4Mask;
                    if (mask is null || Equals(mask, IPAddress.Any))
                        continue;
                    var ipBytes = unicast.Address.GetAddressBytes();
                    var maskBytes = mask.GetAddressBytes();
                    var broadcastBytes = new byte[4];
                    for (var i = 0; i < 4; i++)
                        broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                    addresses.Add(new IPAddress(broadcastBytes));
                }
            }
        }
        catch (NetworkInformationException)
        {
        }
        return addresses.ToArray();
    }

    private void EnsureIdentity()
    {
        if (_certificate is not null) return;
        Directory.CreateDirectory(_directory);
        var identityPath = Path.Combine(_directory, "identity.dat");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!File.Exists(identityPath))
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var request = new CertificateRequest("CN=ClipForge", key, HashAlgorithmName.SHA256);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
                var protectedData = ProtectedData.Protect(certificate.Export(X509ContentType.Pkcs12), null, DataProtectionScope.CurrentUser);
                try
                {
                    using var file = new FileStream(identityPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    file.Write(protectedData);
                }
                catch (IOException) when (File.Exists(identityPath))
                {
                }
            }
            try
            {
                var pfx = ProtectedData.Unprotect(File.ReadAllBytes(identityPath), null, DataProtectionScope.CurrentUser);
                try
                {
                    try
                    {
                        _certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                    }
                    catch (CryptographicException)
                    {
                        _certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pfx);
                }
                if (_certificate.NotAfter <= DateTime.UtcNow)
                {
                    _certificate.Dispose();
                    _certificate = null;
                    File.Delete(identityPath);
                    continue;
                }
                break;
            }
            catch (CryptographicException) when (attempt == 0)
            {
                try { File.Delete(identityPath); } catch (IOException) { }
            }
        }
        if (_certificate is null)
            throw new CryptographicException("No se pudo inicializar el certificado de identidad de red.");
        _fingerprint = FingerprintOf(_certificate);
    }

    private LanSettings LoadSettings()
    {
        try
        {
            var file = Path.Combine(_directory, "settings.json");
            return File.Exists(file) ? JsonSerializer.Deserialize<LanSettings>(File.ReadAllText(file), JsonOptions) ?? new LanSettings() : new LanSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new LanSettings(); }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_settings, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static async Task WriteFrameAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (payload.Length > MaxFrame) throw new InvalidOperationException("El mensaje de red es demasiado grande.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    private static async Task<T> ReadFrameAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaxFrame) throw new InvalidOperationException("El mensaje de red no es válido.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, token);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new JsonException("Mensaje vacío.");
    }

    private static string FingerprintOf(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    private static bool ValidFingerprint(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string CleanName(string? value)
    {
        var clean = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
        return clean.Length == 0 ? "Dispositivo" : clean;
    }
    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 180 || value.Any(char.IsControl) || value != Path.GetFileName(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or ".." || value.EndsWith(' ') || value.EndsWith('.')) return null;
        return value;
    }
    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }
    public void Dispose() { StopCore(); _certificate?.Dispose(); }
    private sealed record Announcement(string Protocol, string Name, string Fingerprint);
    private sealed record TransferHeader(string DeviceName, string FileName, long Length, string? TransferId = null);
    private sealed record TransferReply(bool Accepted, string? Message, long ResumeOffset = 0, bool AlreadyCompleted = false);
}
