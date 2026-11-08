using System.IO;
using System.Text.Json;
using ClickBoard.App.Settings;

namespace ClickBoard.App;

public partial class App
{
    private Dictionary<long,string> _favoriteBindings = [];
    private bool _favoriteBindingsInvalid;
    private bool _favoritePasting;
    private void InitializeFavoriteShortcuts()
    {
        if (_messageHost is null || _mainWindow is null) return;
        try
        {
            _favoriteBindings = FavoriteShortcuts.Load();
            var failed = 0;
            foreach(var pair in _favoriteBindings) if(!_messageHost.TrySetFavoriteHotkey(pair.Key,pair.Value,out _)) failed++;
            if(failed > 0) _mainViewModel!.StatusMessage = $"{failed} atajos de favoritos ocupados o inválidos. Revísalos en Atajos de favoritos.";
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or JsonException)
        { _favoriteBindingsInvalid = true; _mainViewModel!.StatusMessage = "No se pudieron cargar los atajos de favoritos; el archivo se conserva."; }
        _mainWindow.GetFavoriteShortcuts = () => new Dictionary<long,string>(_favoriteBindings);
        _mainWindow.SetFavoriteShortcut = (id, hotkey) =>
        {
            if(_favoriteBindingsInvalid) return "No se puede modificar el archivo de atajos porque no pudo leerse.";
            var previous = _favoriteBindings.GetValueOrDefault(id);
            if(!_messageHost.TrySetFavoriteHotkey(id,hotkey,out var error)) return error;
            var updated = new Dictionary<long,string>(_favoriteBindings);
            if(string.IsNullOrWhiteSpace(hotkey)) updated.Remove(id); else updated[id] = hotkey.Trim();
            try { FavoriteShortcuts.Save(updated); _favoriteBindings = updated; return null; }
            catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
            {
                var restored = _messageHost.TrySetFavoriteHotkey(id,previous,out _);
                return restored ? "No se pudo guardar el atajo. Se conserva el anterior." : "No se pudo guardar ni restaurar el atajo activo. Reinicia para cargar la configuración guardada.";
            }
        };
        _messageHost.FavoriteRequested += async id =>
        {
            if(_favoritePasting || _pasteService is null || _repository is null) return;
            _favoritePasting = true;
            var target = _pasteService.CaptureTargetWindow();
            try
            {
                var clip = await _repository.GetByIdAsync(id);
                if(clip?.IsFavorite != true) { QueueStatus("El clip ya no es favorito o está en la papelera. Revisa su atajo."); return; }
                var sent = await _pasteService.PasteAsync(clip,targetWindow:target);
                if(!sent) QueueStatus("Favorito copiado. No se pudo enviar el pegado al destino; pulsa Ctrl+V.");
            }
            catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or Microsoft.Data.Sqlite.SqliteException or System.Runtime.InteropServices.COMException)
            { QueueStatus("No se pudo pegar el favorito. Comprueba su archivo o vuelve a intentarlo."); }
            finally { _favoritePasting = false; }
        };
    }
}
