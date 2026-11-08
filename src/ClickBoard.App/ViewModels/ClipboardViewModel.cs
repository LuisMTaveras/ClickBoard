using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using ClickBoard.Core.Models;
using ClickBoard.Core.Services;
using ClickBoard.Infrastructure.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClickBoard.App.ViewModels;

public partial class ClipboardViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardRepository _repository;
    private readonly WindowsPasteService _pasteService;
    private CancellationTokenSource? _searchCancellation;
    private long _refreshVersion;
    private readonly bool _loadSelectionPreview;
    private CancellationTokenSource? _previewCancellation;
    private ClipboardClip? _selectedPayload;
    private int _requestedCount = 100;
    private bool _hasMore;
    public string? ActiveProject { get; private set; }
    public string ProjectLabel => ActiveProject is null ? "Todos los proyectos" : "Proyecto: " + ActiveProject;
    public Task<IReadOnlyList<string>> GetProjectsAsync() => _repository.GetProjectsAsync();
    public Task CreateProjectAsync(string name) => _repository.CreateProjectAsync(name);
    public async Task SelectProjectAsync(string? project)
    {
        ActiveProject = project; _requestedCount = 100; OnPropertyChanged(nameof(ProjectLabel)); await RefreshAsync();
    }
    public async Task MoveToProjectAsync(IReadOnlyList<long> ids, string? project)
    {
        await _repository.MoveToProjectAsync(ids, project); await RefreshAsync(); StatusMessage = $"{ids.Count} clips movidos.";
    }

    public ObservableCollection<ClipboardClip> Clips { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ClipboardClip? _selectedClip;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentSection = "Historial";

    [ObservableProperty]
    private string _statusMessage = "Todo permanece en este equipo";

    [ObservableProperty]
    private string _titleDraft = string.Empty;
    [ObservableProperty] private string _collectionDraft = string.Empty;
    [ObservableProperty] private string _tagsDraft = string.Empty;

    private ClipKind? _kindFilter;
    private bool _favoritesOnly;
    private bool _developerOnly;
    private string? _transformedText;
    private bool _jsonFormatted;
    private string? _previousTransformedText;
    private bool _canUndoTransform;
    private static readonly Regex VariablePattern = new(@"\$\{([A-Za-z_][A-Za-z0-9_]{0,39})\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public ObservableCollection<SnippetVariable> Variables { get; } = [];
    public bool HasVariables => Variables.Count > 0;
    public bool CanTransform => _selectedPayload is { Kind: ClipKind.Text, TextContent: not null };
    public bool CanUndoTransform => _canUndoTransform;

    public ClipboardViewModel(IClipboardRepository repository, WindowsPasteService pasteService, bool loadSelectionPreview = true)
    {
        _repository = repository;
        _pasteService = pasteService;
        _loadSelectionPreview = loadSelectionPreview;
    }

    public bool HasClips => Clips.Count > 0;
    public bool HasMore => _hasMore;
    public byte[]? SelectedImageBytes => _selectedPayload?.ImageBytes ?? SelectedClip?.ImageBytes;
    public string SectionDescription => CurrentSection switch
    {
        "Favoritos" => "Los clips que marcaste para tenerlos a mano.",
        "Desarrollo" => "Código, JSON, consultas y comandos de tu historial.",
        "Imágenes" => "Las imágenes copiadas que tienes guardadas.",
        "Historial" => "Explora lo que has copiado recientemente.",
        _ => "Tus clips recientes están listos para usar."
    };
    public bool LastPasteSent { get; private set; }
    public bool HasSelection => SelectedClip is not null;
    public bool CanFormatJson => _selectedPayload?.ContentCategory == ClipboardContentCategory.Json;
    public bool CanPastePlainText => SelectedClip is { Kind: ClipKind.Text, TextContent: not null };
    public bool HasTransformedText => _transformedText is not null;
    public string SelectedPreviewText => _transformedText ?? _selectedPayload?.DetailText ?? SelectedClip?.DetailText ?? string.Empty;
    public string JsonActionLabel => _jsonFormatted ? "Compactar JSON" : "Formatear JSON";
    public string PrimaryPasteLabel => _pasteService.DestinationLabel;
    public void RefreshDestination() => OnPropertyChanged(nameof(PrimaryPasteLabel));
    public string FavoriteActionLabel => SelectedClip?.IsFavorite == true ? "Quitar de favoritos" : "Marcar como favorito";
    public string EmptyTitle => _developerOnly
        ? (string.IsNullOrWhiteSpace(SearchText) ? "Tu espacio de desarrollo está listo" : "No encontramos contenido de desarrollo")
        : _favoritesOnly
            ? "Aún no hay favoritos"
            : string.IsNullOrWhiteSpace(SearchText) ? "Tu historial comienza con una copia" : "No encontramos coincidencias";
    public string EmptyDescription => string.IsNullOrWhiteSpace(SearchText)
        ? _developerOnly
            ? "Aquí aparecerán automáticamente bloques de código, JSON, consultas SQL, comandos y enlaces técnicos que copies."
            : _favoritesOnly
                ? "Marca con una estrella los clips que quieras conservar a mano."
                : "Copia texto, enlaces, imágenes o archivos desde cualquier aplicación. ClipForge los guardará aquí para que vuelvas a encontrarlos."
        : "Prueba con otras palabras o cambia el filtro para ver más elementos.";

    private bool _suppressSearchDebounce;

    public void ResetSearchText()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        if (string.IsNullOrEmpty(SearchText))
            return;
        _suppressSearchDebounce = true;
        try
        {
            SearchText = string.Empty;
        }
        finally
        {
            _suppressSearchDebounce = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _requestedCount = 100;
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        if (!_suppressSearchDebounce)
            _ = DebounceSearchAsync();
    }

    partial void OnCurrentSectionChanged(string value) => OnPropertyChanged(nameof(SectionDescription));

    partial void OnSelectedClipChanged(ClipboardClip? oldValue, ClipboardClip? newValue)
    {
        var sameClip = oldValue is not null && newValue is not null
            && oldValue.Id == newValue.Id
            && oldValue.ContentHash == newValue.ContentHash;

        if (!sameClip)
        {
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
            _previewCancellation = null;
            _selectedPayload = newValue?.IsPayloadLoaded == true ? newValue : null;
        }
        else if (newValue?.IsPayloadLoaded == true)
        {
            _selectedPayload = newValue;
        }

        TitleDraft = newValue?.Title ?? string.Empty;
        CollectionDraft = newValue?.Collection ?? string.Empty;
        TagsDraft = newValue?.Tags ?? string.Empty;
        if (!sameClip)
        {
            _transformedText = null;
            _jsonFormatted = false;
            _previousTransformedText = null;
            _canUndoTransform = false;
            Variables.Clear();
        }
        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(CanTransform));
        OnPropertyChanged(nameof(CanUndoTransform));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanFormatJson));
        OnPropertyChanged(nameof(CanPastePlainText));
        OnPropertyChanged(nameof(HasTransformedText));
        OnPropertyChanged(nameof(SelectedPreviewText));
        OnPropertyChanged(nameof(JsonActionLabel));
        OnPropertyChanged(nameof(PrimaryPasteLabel));
        OnPropertyChanged(nameof(FavoriteActionLabel));
        OnPropertyChanged(nameof(SelectedImageBytes));
        if (_loadSelectionPreview && newValue is { IsPayloadLoaded: false } && _selectedPayload is null)
        {
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
            _previewCancellation = new CancellationTokenSource();
            _ = LoadSelectionAsync(newValue, _previewCancellation.Token);
        }
    }

    private async Task LoadSelectionAsync(ClipboardClip clip, CancellationToken token)
    {
        try
        {
            var payload = await _repository.GetByIdAsync(clip.Id, token);
            if (token.IsCancellationRequested || SelectedClip?.Id != clip.Id) return;
            _selectedPayload = payload;
            var values = Variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal);
            Variables.Clear();
            if (payload?.TextContent is { } text && text.Length < 1000000)
                foreach (var name in VariablePattern.Matches(text).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Take(32))
                    Variables.Add(new SnippetVariable(name) { Value = values.GetValueOrDefault(name, string.Empty) });
            OnPropertyChanged(nameof(HasVariables));
            OnPropertyChanged(nameof(CanTransform));
            OnPropertyChanged(nameof(SelectedPreviewText));
            OnPropertyChanged(nameof(SelectedImageBytes));
            OnPropertyChanged(nameof(CanFormatJson));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (RegexMatchTimeoutException) { StatusMessage = "No se pudieron identificar las variables de esta plantilla."; }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        { if (SelectedClip?.Id == clip.Id) StatusMessage = "No se pudo cargar el contenido del clip."; }
    }

    public async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;
        var version = ++_refreshVersion;
        var query = SearchText;
        var kind = _kindFilter;
        var favorites = _favoritesOnly;
        var developer = _developerOnly;
        var project = ActiveProject;
        IsLoading = true;
        try
        {
            var next = await _repository.SearchAsync(query, kind, favorites, limit: 101, developerOnly: developer, offset: Clips.Count, project: project);
            if (version != _refreshVersion || query != SearchText || kind != _kindFilter || favorites != _favoritesOnly || developer != _developerOnly) return;
            foreach (var clip in next.Take(100)) Clips.Add(clip);
            _requestedCount = Clips.Count;
            _hasMore = next.Count > 100;
            OnPropertyChanged(nameof(HasMore));
            StatusMessage = $"{Clips.Count:N0} clips cargados.";
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        { StatusMessage = "No se pudieron cargar más clips. Vuelve a intentarlo."; }
        finally { if (version == _refreshVersion) IsLoading = false; }
    }

    private async Task<ClipboardClip?> ResolveClipAsync(long id)
    {
        try { return await _repository.GetByIdAsync(id); }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        { StatusMessage = "No se pudo cargar el contenido del clip."; return null; }
    }

    public async Task SetSectionAsync(string section)
    {
        _requestedCount = 100;
        CurrentSection = section;
        _favoritesOnly = section == "Favoritos";
        _kindFilter = section == "Imágenes" ? ClipKind.Image : null;
        _developerOnly = section == "Desarrollo";
        NotifyEmptyStateChanged();
        await RefreshAsync();
    }

    public async Task SetKindFilterAsync(ClipKind? kind)
    {
        _requestedCount = 100;
        _kindFilter = kind;
        _favoritesOnly = false;
        _developerOnly = false;
        NotifyEmptyStateChanged();
        await RefreshAsync();
    }

    public async Task SetDeveloperFilterAsync()
    {
        _requestedCount = 100;
        CurrentSection = "Desarrollo";
        _kindFilter = null;
        _favoritesOnly = false;
        _developerOnly = true;
        NotifyEmptyStateChanged();
        await RefreshAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var version = ++_refreshVersion;
        IsLoading = true;
        try
        {
            var results = await _repository.SearchAsync(
                SearchText,
                _kindFilter,
                _favoritesOnly,
                limit: _requestedCount + 1,
                cancellationToken: cancellationToken, developerOnly: _developerOnly, project: ActiveProject);

            if (cancellationToken.IsCancellationRequested || version != _refreshVersion)
                return;

            var previousId = SelectedClip?.Id;
            var draft = TitleDraft;
            var collectionDraft = CollectionDraft;
            var tagsDraft = TagsDraft;
            var transformed = _transformedText;
            var previousTransform = _previousTransformedText;
            var canUndo = _canUndoTransform;
            var jsonFormatted = _jsonFormatted;
            var variables = Variables.ToArray();
            var hasUnsavedTitle = SelectedClip is not null && draft != (SelectedClip.Title ?? string.Empty);
            var hasUnsavedMetadata = SelectedClip is not null && (collectionDraft != (SelectedClip.Collection ?? string.Empty) || tagsDraft != (SelectedClip.Tags ?? string.Empty));
            Clips.Clear();
            foreach (var clip in results.Take(_requestedCount))
                Clips.Add(clip);
            _hasMore = results.Count > _requestedCount && _requestedCount < 50000;
            OnPropertyChanged(nameof(HasMore));

            SelectedClip = previousId is null
                ? Clips.FirstOrDefault()
                : Clips.FirstOrDefault(clip => clip.Id == previousId) ?? Clips.FirstOrDefault();
            if (hasUnsavedTitle && SelectedClip?.Id == previousId) TitleDraft = draft;
            if (hasUnsavedMetadata && SelectedClip?.Id == previousId) { CollectionDraft = collectionDraft; TagsDraft = tagsDraft; }
            if (previousId is not null && SelectedClip?.Id == previousId)
            {
                _transformedText = transformed;
                _previousTransformedText = previousTransform;
                _canUndoTransform = canUndo;
                _jsonFormatted = jsonFormatted;
                Variables.Clear();
                foreach (var variable in variables) Variables.Add(variable);
                OnPropertyChanged(nameof(HasVariables));
                OnPropertyChanged(nameof(CanUndoTransform));
                OnPropertyChanged(nameof(JsonActionLabel));
                NotifyTransformChanged();
            }

            OnPropertyChanged(nameof(HasClips));
            StatusMessage = Clips.Count == 0
                ? "Copia algo para comenzar a crear tu historial"
                : $"{Clips.Count:N0} elemento{(Clips.Count == 1 ? string.Empty : "s")} guardado{(Clips.Count == 1 ? string.Empty : "s")} en este equipo";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        {
            if (version == _refreshVersion) StatusMessage = "No se pudo cargar el historial. Vuelve a intentarlo.";
        }
        finally
        {
            if (version == _refreshVersion)
                IsLoading = false;
        }
    }

    public async Task PasteClipAsync(ClipboardClip? clip, IntPtr? targetWindow = null)
    {
        var target = targetWindow ?? _pasteService.CaptureTargetWindow();
        LastPasteSent = false;
        if (clip is null)
            return;

        try
        {
            if (!clip.IsPayloadLoaded)
            {
                clip = await _repository.GetByIdAsync(clip.Id);
                if (clip is null) { StatusMessage = "El clip ya no existe."; return; }
            }
            var sent = await _pasteService.PasteAsync(clip, targetWindow: target);
            LastPasteSent = sent;
            StatusMessage = sent ? "Se envió el comando de pegar" : "Clip copiado. Vuelve a la aplicación y pulsa Ctrl+V.";
        }
        catch (PasteBusyException)
        {
            StatusMessage = "Ya hay un pegado en curso.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            StatusMessage = "No se pudo acceder al portapapeles. Inténtalo de nuevo.";
        }
        catch (System.IO.FileNotFoundException)
        {
            StatusMessage = "No se encontró el archivo original; puede que se haya movido o eliminado.";
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            StatusMessage = "No se pudo cargar el contenido del clip para pegarlo.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or JsonException)
        {
            StatusMessage = "No se pudo preparar el contenido para pegarlo. Comprueba el archivo y vuelve a intentarlo.";
        }
    }

    public async Task PasteSelectedPlainTextAsync()
    {
        if (SelectedClip is not { Kind: ClipKind.Text } selected)
            return;
        var target = _pasteService.CaptureTargetWindow();
        var transformed = _transformedText;
        var clip = await ResolveClipAsync(selected.Id);
        if (clip?.TextContent is null) return;

        await PasteClipAsync(clip with
        {
            TextContent = transformed ?? clip.TextContent,
            HtmlContent = null,
            RtfContent = null,
            ImageBytes = null,
            FilesJson = null
        }, target);
    }

    public async Task<ClipboardClip?> GetSelectedContentAsync()
    {
        if (SelectedClip is not { } selected) return null;
        var transformed = _transformedText;
        var clip = await ResolveClipAsync(selected.Id);
        if (clip is null) return null;
        return transformed is null ? clip : clip with { TextContent = transformed, HtmlContent = null, RtfContent = null, ImageBytes = null, FilesJson = null };
    }

    public async Task CopySelectedAsync()
    {
        try
        {
            var clip = await GetSelectedContentAsync();
            if (clip is null) return;
            await _pasteService.CopyAsync(clip);
            StatusMessage = "Clip copiado al portapapeles.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException or System.IO.IOException or UnauthorizedAccessException or JsonException)
        { StatusMessage = "No se pudo copiar el clip. Inténtalo de nuevo."; }
    }

    public async Task CombineTextClipsAsync(IReadOnlyList<ClipboardClip> clips, string? separator = null)
    {
        if (clips.Count < 2 || clips.Any(clip => clip.Kind != ClipKind.Text))
        { StatusMessage = "Selecciona al menos dos clips de texto para unirlos."; return; }
        try
        {
            var parts = new List<string>();
            foreach (var clip in clips)
            {
                var full = await _repository.GetByIdAsync(clip.Id);
                if (full?.TextContent is not { } value) { StatusMessage = "Un clip ya no está disponible; vuelve a seleccionar."; return; }
                parts.Add(value);
            }
            var combined = string.Join(separator ?? Environment.NewLine + Environment.NewLine, parts);
            await _pasteService.CopyAsync(new ClipboardClip { Kind = ClipKind.Text, TextContent = combined, PreviewText = "Textos combinados" });
            StatusMessage = $"{parts.Count} textos unidos y copiados en el orden mostrado.";
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { StatusMessage = "No se pudieron unir los clips. Inténtalo de nuevo."; }
    }

    public async Task<(string Left, string Right)?> GetComparisonAsync(IReadOnlyList<ClipboardClip> clips)
    {
        if (clips.Count != 2 || clips.Any(clip => clip.Kind != ClipKind.Text))
        { StatusMessage = "Selecciona exactamente dos clips de texto para compararlos."; return null; }
        try
        {
            var left = await _repository.GetByIdAsync(clips[0].Id);
            var right = await _repository.GetByIdAsync(clips[1].Id);
            if (left?.TextContent is not { } first || right?.TextContent is not { } second)
            { StatusMessage = "Uno de los clips ya no está disponible."; return null; }
            return (first, second);
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or System.IO.IOException)
        { StatusMessage = "No se pudieron cargar los clips para compararlos."; return null; }
    }

    public async Task PasteSelectedAsync()
    {
        if (SelectedClip is not { } selected)
            return;
        var target = _pasteService.CaptureTargetWindow();
        var transformed = _transformedText;
        var clip = await ResolveClipAsync(selected.Id);
        if (clip is null) { StatusMessage = "El clip ya no existe."; return; }

        if (transformed is not null)
        {
            clip = clip with
            {
                PreviewText = transformed,
                TextContent = transformed,
                HtmlContent = null,
                RtfContent = null,
                ImageBytes = null,
                FilesJson = null
            };
        }

        await PasteClipAsync(clip, target);
    }

    public void ToggleJsonFormatting()
    {
        if (!CanFormatJson || _selectedPayload?.TextContent is not { } original)
            return;

        var source = _transformedText ?? original;
        using var document = JsonDocument.Parse(source);
        RememberTransform();
        _jsonFormatted = !_jsonFormatted;
        _transformedText = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
        {
            WriteIndented = _jsonFormatted
        });
        OnPropertyChanged(nameof(HasTransformedText));
        OnPropertyChanged(nameof(SelectedPreviewText));
        OnPropertyChanged(nameof(JsonActionLabel));
        OnPropertyChanged(nameof(PrimaryPasteLabel));
    }

    private void RememberTransform()
    {
        _previousTransformedText = _transformedText;
        _canUndoTransform = true;
        OnPropertyChanged(nameof(CanUndoTransform));
    }

    public void TransformText(string action)
    {
        if (!CanTransform || _selectedPayload?.TextContent is not { } original) return;
        var source = _transformedText ?? original;
        RememberTransform();
        _transformedText = action switch
        {
            "trim" => source.Trim(),
            "upper" => source.ToUpperInvariant(),
            "lower" => source.ToLowerInvariant(),
            "remove-empty-lines" => string.Join(Environment.NewLine, source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))),
            "distinct-lines" => string.Join(Environment.NewLine, source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Distinct(StringComparer.Ordinal)),
            "template" => VariablePattern.Replace(original, match =>
                Variables.FirstOrDefault(variable => variable.Name == match.Groups[1].Value) is { Value.Length: > 0 } filled
                    ? filled.Value
                    : match.Value),
            "original" => null,
            _ => source
        };
        NotifyTransformChanged();
    }

    public void UndoTransform()
    {
        if (!_canUndoTransform) return;
        _transformedText = _previousTransformedText;
        _previousTransformedText = null;
        _canUndoTransform = false;
        OnPropertyChanged(nameof(CanUndoTransform));
        NotifyTransformChanged();
    }

    public bool ApplyRecipe(ClickBoard.App.Settings.TransformationRecipe recipe)
    {
        if (!CanTransform || _selectedPayload?.TextContent is not { } original) { StatusMessage = "Selecciona un clip de texto y espera a que termine de cargar."; return false; }
        var value = _transformedText ?? original;
        foreach (var action in recipe.Steps)
            value = action switch
            {
                "trim" => value.Trim(), "upper" => value.ToUpperInvariant(), "lower" => value.ToLowerInvariant(),
                "remove-empty-lines" => string.Join(Environment.NewLine, value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))),
                "distinct-lines" => string.Join(Environment.NewLine, value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Distinct(StringComparer.Ordinal)),
                _ => throw new ArgumentException("Paso de receta desconocido.")
            };
        RememberTransform(); _transformedText = value; NotifyTransformChanged();
        StatusMessage = $"Receta «{recipe.Name}» aplicada a la vista previa. Puedes deshacerla."; return true;
    }

    private void NotifyTransformChanged()
    {
        OnPropertyChanged(nameof(HasTransformedText));
        OnPropertyChanged(nameof(SelectedPreviewText));
        OnPropertyChanged(nameof(PrimaryPasteLabel));
    }

    public async Task PasteTransformedTextAsync()
    {
        if (_transformedText is not null) await PasteSelectedAsync();
    }

    public async Task ToggleFavoriteAsync(ClipboardClip? clip)
    {
        if (clip is null)
            return;

        await _repository.SetFavoriteAsync(clip.Id, !clip.IsFavorite);
        await RefreshAsync();
    }

    public async Task SaveTitleAsync()
    {
        if (SelectedClip is not { } clip) return;
        await _repository.SetMetadataAsync(clip.Id, TitleDraft, CollectionDraft, TagsDraft);
        await RefreshAsync();
        StatusMessage = "Título, colección y etiquetas guardados.";
    }

    public async Task<bool> DeleteClipAsync(ClipboardClip clip)
    {
        if (!await _repository.DeleteAsync(clip.Id))
        {
            await RefreshAsync();
            StatusMessage = "Este clip ya no está en el historial.";
            return false;
        }
        await RefreshAsync();
        StatusMessage = "Clip enviado a la papelera.";
        return true;
    }

    public Task<IReadOnlyList<ClipboardClip>> GetTrashAsync() => _repository.GetTrashAsync();
    public Task<ClipboardClip?> LoadClipAsync(long id) => _repository.GetByIdAsync(id);
    public Task<IReadOnlyList<ClipboardClip>> GetExpiredTrashAsync(DateTimeOffset before) => _repository.GetExpiredTrashAsync(before);
    public async Task<bool> PermanentlyDeleteAsync(ClipboardClip clip, DateTimeOffset? deletedBefore = null)
    {
        if (clip.FilesJson is not null)
        {
            var paths = JsonSerializer.Deserialize<string[]>(clip.FilesJson) ?? [];
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                foreach (string? item in System.Windows.Clipboard.GetFileDropList())
                {
                    if (!string.IsNullOrEmpty(item))
                        active.Add(System.IO.Path.GetFullPath(item));
                }
            }
            if (paths.Any(path => active.Contains(System.IO.Path.GetFullPath(path))))
                throw new InvalidOperationException("El portapapeles todavía usa estos archivos.");
        }
        if (!await _repository.PermanentlyDeleteAsync(clip.Id, deletedBefore)) return false;
        return await Task.Run(() => OwnedFileStore.DeleteCopies(clip.FilesJson));
    }
    public async Task RestoreAsync(long id) { await _repository.RestoreAsync(id); await RefreshAsync(); StatusMessage = "Clip restaurado."; }

    private async Task DebounceSearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var token = cancellation.Token;

        try
        {
            await Task.Delay(150, token);
            await RefreshAsync(token);
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this request.
        }
    }

    private void NotifyEmptyStateChanged()
    {
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    public void Dispose()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }
}
