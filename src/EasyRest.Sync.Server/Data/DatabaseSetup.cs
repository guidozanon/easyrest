using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EasyRest.Sync.Server.Data;

/// <summary>Elección de proveedor y de assembly de migraciones, en un solo lugar: lo usan el
/// arranque del server y las herramientas de EF en tiempo de diseño.</summary>
public static class DatabaseSetup
{
    public const string SqliteMigrations = "EasyRest.Sync.Server.Migrations.Sqlite";
    public const string PostgresMigrations = "EasyRest.Sync.Server.Migrations.Postgres";

    public const string DefaultConnectionString = "Data Source=easyrest-sync.db";

    public static bool IsPostgres(string? provider) =>
        provider is "postgres" or "postgresql";

    public static void Configure(DbContextOptionsBuilder builder, string? provider, string connectionString)
    {
        if (IsPostgres(provider?.ToLowerInvariant()))
            builder.UseNpgsql(connectionString, o => o.MigrationsAssembly(PostgresMigrations));
        else
            builder.UseSqlite(connectionString, o => o.MigrationsAssembly(SqliteMigrations));
    }
}

/// <summary>Le da a `dotnet ef` un DbContext sin levantar el server entero. El proveedor sale de
/// Database__Provider, que es lo que hay que setear al generar migraciones de Postgres.</summary>
public class SyncDbContextFactory : IDesignTimeDbContextFactory<SyncDbContext>
{
    public SyncDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "sqlite";
        var builder = new DbContextOptionsBuilder<SyncDbContext>();
        DatabaseSetup.Configure(builder, provider,
            DatabaseSetup.IsPostgres(provider) ? "Host=localhost;Database=easyrest" : "Data Source=design.db");
        return new SyncDbContext(builder.Options);
    }
}
