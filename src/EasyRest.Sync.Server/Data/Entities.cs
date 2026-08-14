namespace EasyRest.Sync.Server.Data;

/// <summary>Rol dentro de un workspace. El orden importa: se compara con &gt;= para chequear
/// permisos (Admin cumple lo que pide Member).</summary>
public enum WorkspaceRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}

/// <summary>Una persona. La identidad la da el IdP: (Provider, Subject) es la clave real —
/// el mail puede cambiar y no es único entre providers.</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

/// <summary>Un workspace: la unidad de compartir. SeqCounter es el cursor monotónico que
/// consumen los clientes para bajar sólo lo que cambió; WrappedKey es la clave de datos del
/// workspace, cifrada con la master key del server (envelope encryption).</summary>
public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid OwnerUserId { get; set; }
    public long SeqCounter { get; set; }
    public byte[] WrappedKey { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
}

/// <summary>Membresía de una persona en un workspace. CanReadSecrets es el default del miembro
/// para todos los ambientes; se puede pisar por ambiente con SecretOverride.</summary>
public class Membership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public bool CanReadSecrets { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Excepción al default de la membresía, para un ambiente puntual.</summary>
public class SecretOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MembershipId { get; set; }
    public Guid DocumentId { get; set; }
    public bool CanRead { get; set; }
}

/// <summary>Un documento sincronizable: una colección, una carpeta, una request o un ambiente.
/// Path es la clave estable dentro del workspace (la misma ruta relativa que en disco).
/// Content nunca lleva valores de secretos: esos van cifrados en SecretValue.</summary>
public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Content { get; set; } = "";
    public string Rev { get; set; } = "";
    public bool Deleted { get; set; }
    public long Seq { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>El valor de una variable secreta, cifrado con la clave del workspace. Vive aparte
/// del documento a propósito: así un GET del ambiente o un export no lo puede arrastrar.</summary>
public class SecretValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DocumentId { get; set; }
    public string Key { get; set; } = "";
    public byte[] Nonce { get; set; } = Array.Empty<byte>();
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
    public byte[] Tag { get; set; } = Array.Empty<byte>();
}

/// <summary>Invitación a un workspace. Del token sólo se guarda el hash: si te roban la base,
/// no te roban las invitaciones pendientes.</summary>
public class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string? Email { get; set; }
    public string TokenHash { get; set; } = "";
    public WorkspaceRole Role { get; set; }
    public bool CanReadSecrets { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>Token de servicio para CI y uso headless: no hay persona detrás, así que se ata
/// directo a un workspace con su rol.</summary>
public class ServiceToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public WorkspaceRole Role { get; set; }
    public bool CanReadSecrets { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public bool Revoked { get; set; }
}

/// <summary>Un login en curso. Guarda el PKCE challenge del cliente hasta que el IdP vuelve, y
/// después el hash del authorization code hasta que la app lo canjea.</summary>
public class AuthFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = "";
    public string Provider { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ClientState { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string? AuthCodeHash { get; set; }
    public Guid? UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Consumed { get; set; }
}

/// <summary>Sesión de una app: par access/refresh. Se guardan hasheados y se pueden revocar,
/// que es la ventaja de usar tokens opacos en vez de JWT en un server self-hosted.</summary>
public class SessionToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string AccessHash { get; set; } = "";
    public string RefreshHash { get; set; } = "";
    public DateTime AccessExpiresAt { get; set; }
    public DateTime RefreshExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public DateTime CreatedAt { get; set; }
}
