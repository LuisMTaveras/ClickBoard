using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClickBoard.Core.Models;
using ClickBoard.Core.Services;
using Microsoft.Data.Sqlite;
using ClickBoard.Infrastructure.Windows;

namespace ClickBoard.Infrastructure.Storage;

public sealed class SqliteClipboardRepository : IClipboardRepository
{
    private readonly string _connectionString;
    public SqliteClipboardRepository()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBoard");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "history.db"), Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = true }.ToString();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS projects(name TEXT PRIMARY KEY COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS clips (
              id INTEGER PRIMARY KEY AUTOINCREMENT, kind INTEGER NOT NULL, preview_text TEXT NOT NULL,
              text_content TEXT, html_content TEXT, rtf_content TEXT, image_bytes BLOB, files_json TEXT,
              created_utc TEXT NOT NULL, source_app TEXT, is_favorite INTEGER NOT NULL DEFAULT 0,
              content_hash TEXT NOT NULL UNIQUE);
            CREATE INDEX IF NOT EXISTS idx_clips_created ON clips(created_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_clips_favorite ON clips(is_favorite, created_utc DESC);
            CREATE VIRTUAL TABLE IF NOT EXISTS clips_fts USING fts5(preview_text,text_content,content='clips',content_rowid='id',tokenize='unicode61 remove_diacritics 2');
            CREATE TRIGGER IF NOT EXISTS clips_ai AFTER INSERT ON clips BEGIN INSERT INTO clips_fts(rowid,preview_text,text_content) VALUES(new.id,new.preview_text,new.text_content); END;
            CREATE TRIGGER IF NOT EXISTS clips_ad AFTER DELETE ON clips BEGIN INSERT INTO clips_fts(clips_fts,rowid,preview_text,text_content) VALUES('delete',old.id,old.preview_text,old.text_content); END;
            DROP TRIGGER IF EXISTS clips_au;
            CREATE TRIGGER clips_au AFTER UPDATE OF preview_text,text_content ON clips
            WHEN old.preview_text IS NOT new.preview_text OR old.text_content IS NOT new.text_content BEGIN
              INSERT INTO clips_fts(clips_fts,rowid,preview_text,text_content) VALUES('delete',old.id,old.preview_text,old.text_content);
              INSERT INTO clips_fts(rowid,preview_text,text_content) VALUES(new.id,new.preview_text,new.text_content); END;
            """;
        command.ExecuteNonQuery();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        command.CommandText = "PRAGMA table_info(clips)";
        using (var reader = command.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(1));
        foreach (var (name, type) in new[] { ("title", "TEXT"), ("collection", "TEXT"), ("tags", "TEXT"), ("category", "INTEGER"), ("thumbnail_bytes", "BLOB"), ("deleted_utc", "TEXT") })
        {
            if (columns.Contains(name)) continue;
            command.CommandText = $"ALTER TABLE clips ADD COLUMN {name} {type}";
            command.ExecuteNonQuery();
        }
        command.CommandText = "CREATE INDEX IF NOT EXISTS idx_clips_category ON clips(category); CREATE INDEX IF NOT EXISTS idx_clips_collection ON clips(collection)";
        command.ExecuteNonQuery();
    }, cancellationToken);

    // True means the caller's payload is retained. False means an existing intact copy was reused.
    public Task<bool> SaveAsync(ClipboardClip clip, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT files_json FROM clips WHERE content_hash=$hash";
        command.Parameters.AddWithValue("$hash", clip.ContentHash);
        bool exists;
        string? oldFiles;
        using (var reader = command.ExecuteReader())
        {
            exists = reader.Read();
            oldFiles = exists && !reader.IsDBNull(0) ? reader.GetString(0) : null;
        }
        var retainNew = !exists;
        if (exists && clip.Kind == ClipKind.Files)
        {
            try
            {
                retainNew = oldFiles is null
                    || JsonSerializer.Deserialize<string[]>(oldFiles) is not { Length: > 0 } files
                    || files.Any(path => !File.Exists(path));
            }
            catch (JsonException)
            {
                retainNew = true;
            }
        }
        command.Parameters.Clear();
        if (exists)
        {
            command.CommandText = "UPDATE clips SET deleted_utc=NULL,created_utc=$created,source_app=$source,files_json=CASE WHEN $replace=1 THEN $files ELSE files_json END WHERE content_hash=$hash";
            command.Parameters.AddWithValue("$replace", retainNew ? 1 : 0);
        }
        else
        {
            command.CommandText = """
                INSERT INTO clips(kind,preview_text,text_content,html_content,rtf_content,image_bytes,files_json,created_utc,source_app,content_hash,category,thumbnail_bytes)
                VALUES($kind,$preview,$text,$html,$rtf,$image,$files,$created,$source,$hash,$category,$thumbnail)
                """;
            command.Parameters.AddWithValue("$kind", (int)clip.Kind);
            command.Parameters.AddWithValue("$preview", clip.PreviewText);
            command.Parameters.AddWithValue("$text", (object?)clip.TextContent ?? DBNull.Value);
            command.Parameters.AddWithValue("$html", (object?)clip.HtmlContent ?? DBNull.Value);
            command.Parameters.AddWithValue("$rtf", (object?)clip.RtfContent ?? DBNull.Value);
            command.Parameters.AddWithValue("$image", (object?)clip.ImageBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$category", (int)clip.ContentCategory);
            command.Parameters.AddWithValue("$thumbnail", (object?)clip.ThumbnailBytes ?? DBNull.Value);
        }
        command.Parameters.AddWithValue("$files", (object?)clip.FilesJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", clip.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$source", (object?)clip.SourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", clip.ContentHash);
        command.ExecuteNonQuery();
        transaction.Commit();

        if (exists && clip.Kind == ClipKind.Files && retainNew && !string.IsNullOrEmpty(oldFiles) && oldFiles != clip.FilesJson)
        {
            OwnedFileStore.DeleteCopies(oldFiles);
        }

        return retainNew;
    }, cancellationToken);

    public Task<ClipboardClip?> GetByIdAsync(long clipId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Projection(true)} FROM clips c WHERE c.id=$id AND c.deleted_utc IS NULL";
        command.Parameters.AddWithValue("$id", clipId);
        ClipboardClip? clip;
        using (var reader = command.ExecuteReader()) clip = reader.Read() ? ReadClip(reader, true) : null;
        if (clip is { Kind: ClipKind.Image, ThumbnailBytes: null, ImageBytes: { } image })
        {
            var thumbnail = ImageThumbnail.Create(image);
            if (thumbnail is not null)
            {
                command.CommandText = "UPDATE clips SET thumbnail_bytes=$thumbnail WHERE id=$id";
                command.Parameters.AddWithValue("$thumbnail", thumbnail);
                command.ExecuteNonQuery();
                clip = clip with { ThumbnailBytes = thumbnail };
            }
        }
        return clip;
    }, cancellationToken);

    public Task<IReadOnlyList<ClipboardClip>> SearchAsync(string query, ClipKind? kind, bool favoritesOnly, int limit = 100,
        CancellationToken cancellationToken = default, bool developerOnly = false, int offset = 0, string? project = null) => Task.Run<IReadOnlyList<ClipboardClip>>(() =>
    {
        var fts = BuildFtsQuery(query);
        try { return SearchCore(fts, query.Trim(), kind, favoritesOnly, limit, developerOnly, offset, project); }
        catch (SqliteException) when (fts is not null) { return SearchCore(null, query.Trim(), kind, favoritesOnly, limit, developerOnly, offset, project); }
    }, cancellationToken);

    private IReadOnlyList<ClipboardClip> SearchCore(string? fts, string query, ClipKind? kind, bool favoritesOnly, int limit, bool developerOnly, int offset, string? project)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var parsed = ClipSearchQuery.Parse(query);
        query = parsed.Text;
        fts = fts is null ? null : BuildFtsQuery(query);
        var filters = new List<string>();
        foreach (var (field, value) in parsed.Filters)
        {
            var parameter = "$filter" + filters.Count;
            string predicate;
            object argument = value;
            switch (field)
            {
                case "app": predicate = $"c.source_app LIKE {parameter} ESCAPE '\\'"; argument = BuildLikePattern(value); break;
                case "etiqueta": predicate = $"c.tags LIKE {parameter} ESCAPE '\\'"; argument = BuildLikePattern(value); break;
                case "proyecto": predicate = $"c.collection = {parameter} COLLATE NOCASE"; break;
                case "antes": predicate = $"c.created_utc < {parameter}"; break;
                default:
                    predicate = value.ToLowerInvariant() switch
                    {
                        "texto" => "c.kind=0", "imagen" => "c.kind=1", "archivo" => "c.kind=2",
                        _ => $"EXISTS (SELECT 1 FROM json_each(c.files_json) file WHERE lower(file.value) LIKE {parameter} ESCAPE '\\')"
                    };
                    if (predicate.Contains(parameter)) argument = "%" + value.TrimStart('.').ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Insert(0, ".");
                    break;
            }
            filters.Add(predicate);
            if (predicate.Contains(parameter)) command.Parameters.AddWithValue(parameter, argument);
        }
        var metadata = "(c.title LIKE $like ESCAPE '\\' OR c.collection LIKE $like ESCAPE '\\' OR c.tags LIKE $like ESCAPE '\\')";
        var condition = string.IsNullOrEmpty(query) ? "1=1" : fts is not null
            ? $"(c.id IN (SELECT rowid FROM clips_fts WHERE clips_fts MATCH $fts) OR {metadata})"
            : $"(c.preview_text LIKE $like ESCAPE '\\' OR c.text_content LIKE $like ESCAPE '\\' OR {metadata})";
        command.CommandText = $"""
            SELECT {Projection(false)} FROM clips c
            WHERE c.deleted_utc IS NULL AND ({(filters.Count == 0 ? "1=1" : string.Join(" AND ", filters))}) AND ({condition}) AND ($kind IS NULL OR c.kind=$kind) AND ($favorites=0 OR c.is_favorite=1)
            AND ($project IS NULL OR c.collection=$project COLLATE NOCASE)
            AND ($dev=0 OR COALESCE(c.category,clip_category(c.kind,c.text_content)) IN (1,2,3,4,5))
            ORDER BY c.is_favorite DESC,c.created_utc DESC,c.id DESC LIMIT $limit OFFSET $offset
            """;
        if (fts is not null && query.Length > 0) command.Parameters.AddWithValue("$fts", fts);
        if (query.Length > 0) command.Parameters.AddWithValue("$like", BuildLikePattern(query));
        command.Parameters.AddWithValue("$kind", kind is null ? DBNull.Value : (object)(int)kind);
        command.Parameters.AddWithValue("$favorites", favoritesOnly ? 1 : 0);
        command.Parameters.AddWithValue("$dev", developerOnly ? 1 : 0);
        command.Parameters.AddWithValue("$project", (object?)project ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50001));
        command.Parameters.AddWithValue("$offset", Math.Max(offset, 0));
        using var reader = command.ExecuteReader();
        var results = new List<ClipboardClip>();
        while (reader.Read()) results.Add(ReadClip(reader, false));
        return results;
    }

    private static string Projection(bool full) => $"c.id,c.kind,c.preview_text,{(full ? "c.text_content,c.html_content,c.rtf_content,c.image_bytes" : "substr(c.text_content,1,2048),NULL,NULL,NULL")},c.files_json,c.created_utc,c.source_app,c.is_favorite,c.content_hash,c.title,c.collection,c.tags,COALESCE(c.category,clip_category(c.kind,c.text_content)),c.thumbnail_bytes";
    private static ClipboardClip ReadClip(SqliteDataReader r, bool full) => new()
    {
        Id=r.GetInt64(0), Kind=(ClipKind)r.GetInt32(1), PreviewText=r.GetString(2), TextContent=r.IsDBNull(3)?null:r.GetString(3),
        HtmlContent=r.IsDBNull(4)?null:r.GetString(4), RtfContent=r.IsDBNull(5)?null:r.GetString(5), ImageBytes=r.IsDBNull(6)?null:(byte[])r[6],
        FilesJson=r.IsDBNull(7)?null:r.GetString(7), CreatedAtUtc=DateTimeOffset.Parse(r.GetString(8)), SourceApp=r.IsDBNull(9)?null:r.GetString(9),
        IsFavorite=r.GetInt32(10)!=0, ContentHash=r.GetString(11), Title=r.IsDBNull(12)?null:r.GetString(12), Collection=r.IsDBNull(13)?null:r.GetString(13),
        Tags=r.IsDBNull(14)?null:r.GetString(14), Category=(ClipboardContentCategory)r.GetInt32(15), ThumbnailBytes=r.IsDBNull(16)?null:(byte[])r[16], IsPayloadLoaded=full
    };
    public Task SetFavoriteAsync(long id,bool favorite,CancellationToken cancellationToken=default) => UpdateAsync("UPDATE clips SET is_favorite=$value WHERE id=$id",id,favorite?1:0,cancellationToken);
    public Task SetTitleAsync(long id,string? title,CancellationToken cancellationToken=default) => UpdateAsync("UPDATE clips SET title=$value WHERE id=$id",id,Normalize(title),cancellationToken);
    public Task SetMetadataAsync(long clipId,string? title,string? collection,string? tags,CancellationToken cancellationToken=default) => Task.Run(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand();
        command.CommandText="UPDATE clips SET title=$title,collection=$collection,tags=$tags WHERE id=$id";
        command.Parameters.AddWithValue("$id",clipId); command.Parameters.AddWithValue("$title",Normalize(title));
        command.Parameters.AddWithValue("$collection",Normalize(collection)); command.Parameters.AddWithValue("$tags",Normalize(tags)); command.ExecuteNonQuery();
    },cancellationToken);
    public Task<bool> DeleteAsync(long clipId,CancellationToken cancellationToken=default) => Task.Run(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand(); command.CommandText="UPDATE clips SET deleted_utc=$deleted WHERE id=$id AND deleted_utc IS NULL";
        command.Parameters.AddWithValue("$deleted",DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id",clipId); return command.ExecuteNonQuery()>0;
    },cancellationToken);
    public Task<IReadOnlyList<ClipboardClip>> GetTrashAsync(CancellationToken cancellationToken=default) => Task.Run<IReadOnlyList<ClipboardClip>>(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand();
        command.CommandText=$"SELECT {Projection(false)} FROM clips c WHERE c.deleted_utc IS NOT NULL ORDER BY c.deleted_utc DESC LIMIT 1000";
        using var reader=command.ExecuteReader(); var results=new List<ClipboardClip>();
        while(reader.Read()) results.Add(ReadClip(reader,false)); return results;
    },cancellationToken);
    public Task<bool> RestoreAsync(long clipId,CancellationToken cancellationToken=default) => Task.Run(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand();
        command.CommandText="UPDATE clips SET deleted_utc=NULL WHERE id=$id AND deleted_utc IS NOT NULL";
        command.Parameters.AddWithValue("$id",clipId); return command.ExecuteNonQuery()>0;
    },cancellationToken);
    public Task<bool> PermanentlyDeleteAsync(long clipId, DateTimeOffset? deletedBefore = null) => Task.Run(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand();
        command.CommandText="DELETE FROM clips WHERE id=$id AND deleted_utc IS NOT NULL AND ($before IS NULL OR deleted_utc<$before)";
        command.Parameters.AddWithValue("$before", (object?)deletedBefore?.ToUniversalTime().ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$id",clipId); return command.ExecuteNonQuery()>0;
    });
    public Task<IReadOnlyList<ClipboardClip>> GetExpiredTrashAsync(DateTimeOffset before) => Task.Run<IReadOnlyList<ClipboardClip>>(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand();
        command.CommandText=$"SELECT {Projection(false)} FROM clips c WHERE c.deleted_utc<$before ORDER BY c.deleted_utc LIMIT 1000";
        command.Parameters.AddWithValue("$before",before.ToUniversalTime().ToString("O"));
        using var reader=command.ExecuteReader(); var results=new List<ClipboardClip>(); while(reader.Read()) results.Add(ReadClip(reader,false)); return results;
    });
    private Task UpdateAsync(string sql,long id,object value,CancellationToken token) => Task.Run(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand(); command.CommandText=sql;
        command.Parameters.AddWithValue("$id",id); command.Parameters.AddWithValue("$value",value); command.ExecuteNonQuery();
    },token);
    public Task<IReadOnlyList<ClipboardClip>> GetCleanupCandidatesAsync(DateTimeOffset before, CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<ClipboardClip>>(() =>
    {
        using var connection = OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Projection(false)} FROM clips c WHERE c.deleted_utc IS NULL AND c.created_utc < $before AND c.is_favorite=0 AND trim(COALESCE(c.title,''))='' AND trim(COALESCE(c.collection,''))='' AND trim(COALESCE(c.tags,''))='' ORDER BY c.created_utc LIMIT 1000";
        command.Parameters.AddWithValue("$before", before.ToUniversalTime().ToString("O"));
        var results = new List<ClipboardClip>(); using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadClip(reader, false));
        return results;
    }, cancellationToken);
    public Task<bool> DeleteIfUnprotectedAsync(long clipId, DateTimeOffset before, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var connection = OpenConnection(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clips WHERE id=$id AND created_utc<$before AND is_favorite=0 AND trim(COALESCE(title,''))='' AND trim(COALESCE(collection,''))='' AND trim(COALESCE(tags,''))=''";
        command.Parameters.AddWithValue("$id", clipId); command.Parameters.AddWithValue("$before", before.ToUniversalTime().ToString("O"));
        return command.ExecuteNonQuery() > 0;
    }, cancellationToken);
    private static object Normalize(string? text) => string.IsNullOrWhiteSpace(text)?DBNull.Value:text.Trim();
    public Task<IReadOnlyList<string>> GetProjectsAsync() => Task.Run<IReadOnlyList<string>>(() =>
    {
        using var connection=OpenConnection(); using var command=connection.CreateCommand();
        command.CommandText="SELECT name FROM projects UNION SELECT collection FROM clips WHERE collection IS NOT NULL AND trim(collection)<>'' ORDER BY 1 COLLATE NOCASE";
        using var reader=command.ExecuteReader(); var names=new List<string>(); while(reader.Read()) names.Add(reader.GetString(0)); return names;
    });
    public Task CreateProjectAsync(string name) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length>80) throw new ArgumentException("Nombre de proyecto inválido.");
        using var connection=OpenConnection(); using var command=connection.CreateCommand(); command.CommandText="INSERT OR IGNORE INTO projects(name) VALUES($name)";
        command.Parameters.AddWithValue("$name",name.Trim()); command.ExecuteNonQuery();
    });
    public Task MoveToProjectAsync(IReadOnlyList<long> ids, string? project) => Task.Run(() =>
    {
        if (project is not null && (string.IsNullOrWhiteSpace(project) || project.Length>80)) throw new ArgumentException("Nombre de proyecto inválido.");
        using var connection=OpenConnection(); using var transaction=connection.BeginTransaction(); using var command=connection.CreateCommand(); command.Transaction=transaction;
        command.CommandText="UPDATE clips SET collection=$project WHERE id=$id AND deleted_utc IS NULL";
        command.Parameters.AddWithValue("$project",(object?)project ?? DBNull.Value); var parameter=command.Parameters.Add("$id",SqliteType.Integer);
        foreach(var id in ids) { parameter.Value=id; command.ExecuteNonQuery(); } transaction.Commit();
    });
    private SqliteConnection OpenConnection()
    {
        var connection=new SqliteConnection(_connectionString); connection.Open();
        connection.CreateFunction<long,string?,long>("clip_category",(kind,text)=>(long)ClipboardContentClassifier.Classify((ClipKind)kind,text),isDeterministic:true);
        return connection;
    }
    private static string? BuildFtsQuery(string query)
    {
        var terms=Regex.Matches(query,"[\\p{L}\\p{N}_]+").Select(m=>m.Value).Take(12).Select(s=>$"\"{s}\"*").ToArray();
        return terms.Length==0?null:string.Join(" AND ",terms);
    }
    private static string BuildLikePattern(string text)=>"%"+text.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
}

