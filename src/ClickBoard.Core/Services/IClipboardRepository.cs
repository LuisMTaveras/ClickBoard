using ClickBoard.Core.Models;

namespace ClickBoard.Core.Services;

public interface IClipboardRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveAsync(ClipboardClip clip, CancellationToken cancellationToken = default);
    Task<ClipboardClip?> GetByIdAsync(long clipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClipboardClip>> SearchAsync(
        string query,
        ClipKind? kind,
        bool favoritesOnly,
        int limit = 500,
        CancellationToken cancellationToken = default, bool developerOnly = false, int offset = 0, string? project = null);
    Task<IReadOnlyList<string>> GetProjectsAsync();
    Task CreateProjectAsync(string name);
    Task MoveToProjectAsync(IReadOnlyList<long> ids, string? project);
    Task SetMetadataAsync(long clipId, string? title, string? collection, string? tags, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(long clipId, bool isFavorite, CancellationToken cancellationToken = default);
    Task SetTitleAsync(long clipId, string? title, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long clipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClipboardClip>> GetTrashAsync(CancellationToken cancellationToken = default);
    Task<bool> RestoreAsync(long clipId, CancellationToken cancellationToken = default);
    Task<bool> PermanentlyDeleteAsync(long clipId, DateTimeOffset? deletedBefore = null);
    Task<IReadOnlyList<ClipboardClip>> GetExpiredTrashAsync(DateTimeOffset before);
    Task<IReadOnlyList<ClipboardClip>> GetCleanupCandidatesAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
    Task<bool> DeleteIfUnprotectedAsync(long clipId, DateTimeOffset before, CancellationToken cancellationToken = default);
}
