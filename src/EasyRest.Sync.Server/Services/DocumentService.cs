using System.Collections.Concurrent;
using System.Text.Json;
using EasyRest.Sync.Server.Crypto;
using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Services;

public enum WriteOutcome { Ok, Conflict, NotFound, Invalid }

public record WriteResult(WriteOutcome Outcome, Document? Document, string? Error = null)
{
    public static WriteResult Ok(Document doc) => new(WriteOutcome.Ok, doc);
    public static WriteResult Conflict(Document current) => new(WriteOutcome.Conflict, current);
    public static WriteResult NotFound() => new(WriteOutcome.NotFound, null);
    public static WriteResult Invalid(string error) => new(WriteOutcome.Invalid, null, error);
}

/// <summary>Documentos y su sincronización. El modelo es deliberadamente simple: cada documento
/// tiene una revisión opaca y un número de secuencia dentro del workspace; el cliente se guarda
/// la última secuencia que vio y pide sólo lo posterior.</summary>
public class DocumentService(SyncDbContext db, SecretBox secretBox, WorkspaceService workspaces)
{
    /// <summary>Las escrituras de un workspace se serializan en memoria para que la asignación
    /// de secuencia sea atómica. Alcanza porque el server está pensado para correr en una sola
    /// instancia (ver docs/SYNC.md); el token de concurrencia sobre SeqCounter queda igual como
    /// red de seguridad.</summary>
    static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public const string EnvironmentKind = "environment";

    public async Task<WriteResult> PutAsync(WorkspaceAccess access, PutDocumentRequest request,
        string? ifMatch, CancellationToken ct)
    {
        var path = (request.Path ?? "").Replace('\\', '/').Trim('/');
        if (path.Length == 0) return WriteResult.Invalid("El documento necesita un path.");
        if (path.Contains("..")) return WriteResult.Invalid("El path no puede salir del workspace.");
        if (request.Content == null) return WriteResult.Invalid("El documento necesita content.");

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "request" : request.Kind.Trim();
        if (kind == EnvironmentKind && ValidateEnvironment(request.Content) is { } error)
            return WriteResult.Invalid(error);

        var gate = Locks.GetOrAdd(access.Workspace.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await db.Documents
                .FirstOrDefaultAsync(d => d.WorkspaceId == access.Workspace.Id && d.Path == path, ct);

            if (MatchFails(ifMatch, existing)) return WriteResult.Conflict(existing!);

            var workspace = await db.Workspaces.FirstAsync(w => w.Id == access.Workspace.Id, ct);
            workspace.SeqCounter++;

            var doc = existing ?? new Document { WorkspaceId = workspace.Id, Path = path };
            if (existing == null) db.Documents.Add(doc);

            doc.Kind = kind;
            doc.Content = request.Content;
            doc.Deleted = false;
            doc.Rev = Tokens.NewRev();
            doc.Seq = workspace.SeqCounter;
            doc.UpdatedAt = DateTime.UtcNow;
            doc.UpdatedByUserId = access.UserId;

            await db.SaveChangesAsync(ct);

            // los secretos se escriben aparte y sólo si el caller puede verlos: si no, se dejan
            // los que ya estaban (un miembro sin permiso puede editar el resto del ambiente sin
            // pisar los tokens de nadie)
            if (request.Secrets != null && await workspaces.CanAccessSecretsAsync(access, doc.Id, ct))
                await ReplaceSecretsAsync(workspace, doc, request.Secrets, ct);

            return WriteResult.Ok(doc);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<WriteResult> DeleteAsync(WorkspaceAccess access, Guid documentId, string? ifMatch,
        CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(access.Workspace.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var doc = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == access.Workspace.Id, ct);
            if (doc == null || doc.Deleted) return WriteResult.NotFound();
            if (MatchFails(ifMatch, doc)) return WriteResult.Conflict(doc);

            var workspace = await db.Workspaces.FirstAsync(w => w.Id == access.Workspace.Id, ct);
            workspace.SeqCounter++;

            // tombstone: el documento se vacía pero la fila queda, para que los otros
            // dispositivos se enteren del borrado en su próximo delta
            doc.Deleted = true;
            doc.Content = "";
            doc.Rev = Tokens.NewRev();
            doc.Seq = workspace.SeqCounter;
            doc.UpdatedAt = DateTime.UtcNow;
            doc.UpdatedByUserId = access.UserId;

            var secrets = await db.SecretValues.Where(s => s.DocumentId == doc.Id).ToListAsync(ct);
            db.SecretValues.RemoveRange(secrets);

            await db.SaveChangesAsync(ct);
            return WriteResult.Ok(doc);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Delta desde un cursor. Devuelve los documentos en orden de secuencia, tombstones
    /// incluidos, y el cursor nuevo.</summary>
    public async Task<ChangesResponse> ChangesAsync(WorkspaceAccess access, long since, int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);

        var docs = await db.Documents
            .Where(d => d.WorkspaceId == access.Workspace.Id && d.Seq > since)
            .OrderBy(d => d.Seq)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = docs.Count > limit;
        if (hasMore) docs.RemoveAt(docs.Count - 1);

        var cursor = docs.Count > 0 ? docs[^1].Seq : since;
        return new ChangesResponse(cursor, hasMore, docs.Select(ToResponse).ToArray());
    }

    public async Task<Dictionary<string, string>> ReadSecretsAsync(WorkspaceAccess access, Document doc,
        CancellationToken ct)
    {
        var rows = await db.SecretValues
            .Where(s => s.DocumentId == doc.Id)
            .OrderBy(s => s.Key)
            .ToListAsync(ct);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
            result[row.Key] = secretBox.Open(
                access.Workspace.WrappedKey, doc.Id, row.Key, row.Nonce, row.Ciphertext, row.Tag);
        return result;
    }

    async Task ReplaceSecretsAsync(Workspace workspace, Document doc,
        Dictionary<string, string> secrets, CancellationToken ct)
    {
        var existing = await db.SecretValues.Where(s => s.DocumentId == doc.Id).ToListAsync(ct);
        db.SecretValues.RemoveRange(existing);

        foreach (var (key, value) in secrets)
        {
            if (string.IsNullOrEmpty(key)) continue;
            var (nonce, cipher, tag) = secretBox.Seal(workspace.WrappedKey, doc.Id, key, value ?? "");
            db.SecretValues.Add(new SecretValue
            {
                WorkspaceId = workspace.Id,
                DocumentId = doc.Id,
                Key = key,
                Nonce = nonce,
                Ciphertext = cipher,
                Tag = tag
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>If-Match ausente = crear (falla si ya hay un documento vivo en ese path).
    /// "*" = pisar sin preguntar. Cualquier otro valor tiene que ser la revisión actual.</summary>
    static bool MatchFails(string? ifMatch, Document? existing)
    {
        var expected = (ifMatch ?? "").Trim().Trim('"');

        if (expected.Length == 0) return existing is { Deleted: false };
        if (expected == "*") return false;
        return existing == null || existing.Rev != expected;
    }

    /// <summary>Los ambientes son el único tipo con reglas propias: el server se asegura de que
    /// los valores marcados como secretos no viajen dentro del content, que es lo que ven todos
    /// los miembros. Si no, el permiso de secretos sería decorativo.</summary>
    internal static string? ValidateEnvironment(string content)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(content);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return "El ambiente no es JSON válido.";
        }

        if (root.ValueKind != JsonValueKind.Object) return "El ambiente tiene que ser un objeto JSON.";
        if (!root.TryGetProperty("secretKeys", out var secretKeys) ||
            secretKeys.ValueKind != JsonValueKind.Array)
            return null;

        var secret = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in secretKeys.EnumerateArray())
            if (k.ValueKind == JsonValueKind.String) secret.Add(k.GetString()!);
        if (secret.Count == 0) return null;

        if (!root.TryGetProperty("variables", out var variables) || variables.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var variable in variables.EnumerateArray())
        {
            if (variable.ValueKind != JsonValueKind.Object) continue;
            var key = variable.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (key == null || !secret.Contains(key)) continue;

            var value = variable.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
            if (!string.IsNullOrEmpty(value))
                return $"La variable '{key}' está marcada como secreta: su valor va en 'secrets', " +
                       "no dentro del documento.";
        }

        return null;
    }

    public static DocumentResponse ToResponse(Document d) => new(
        d.Id, d.Path, d.Kind, d.Deleted ? null : d.Content, d.Rev, d.Deleted, d.Seq, d.UpdatedAt);
}
