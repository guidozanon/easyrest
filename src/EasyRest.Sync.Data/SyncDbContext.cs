using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Data;

public class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<SecretOverride> SecretOverrides => Set<SecretOverride>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<SecretValue> SecretValues => Set<SecretValue>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ServiceToken> ServiceTokens => Set<ServiceToken>();
    public DbSet<AuthFlow> AuthFlows => Set<AuthFlow>();
    public DbSet<SessionToken> SessionTokens => Set<SessionToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(64);
            e.Property(x => x.Subject).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(256);
        });

        b.Entity<Workspace>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            // el cursor se incrementa en cada escritura: el token de concurrencia hace que dos
            // writers simultáneos no puedan asignar el mismo Seq (uno reintenta)
            e.Property(x => x.SeqCounter).IsConcurrencyToken();
        });

        b.Entity<Membership>(e =>
        {
            e.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<SecretOverride>(e => e.HasIndex(x => new { x.MembershipId, x.DocumentId }).IsUnique());

        b.Entity<Document>(e =>
        {
            e.HasIndex(x => new { x.WorkspaceId, x.Path }).IsUnique();
            // el delta de sync es exactamente este índice
            e.HasIndex(x => new { x.WorkspaceId, x.Seq });
            e.Property(x => x.Path).HasMaxLength(1024);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Rev).HasMaxLength(64);
        });

        b.Entity<SecretValue>(e =>
        {
            e.HasIndex(x => new { x.DocumentId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(256);
        });

        b.Entity<Invitation>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(320);
        });

        b.Entity<ServiceToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<AuthFlow>(e =>
        {
            e.HasIndex(x => x.State).IsUnique();
            e.HasIndex(x => x.AuthCodeHash);
            e.Property(x => x.State).HasMaxLength(64);
            e.Property(x => x.AuthCodeHash).HasMaxLength(64);
        });

        b.Entity<SessionToken>(e =>
        {
            e.HasIndex(x => x.AccessHash);
            e.HasIndex(x => x.RefreshHash);
            e.Property(x => x.AccessHash).HasMaxLength(64);
            e.Property(x => x.RefreshHash).HasMaxLength(64);
        });
    }
}
