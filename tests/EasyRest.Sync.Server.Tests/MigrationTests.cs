using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

/// <summary>Guarda de regresión del camino de actualización: si alguien vuelve a EnsureCreated,
/// el esquema se crea igual y todo lo demás sigue pasando, pero las instalaciones existentes
/// dejan de poder actualizarse. Estos tests lo detectan.</summary>
public class MigrationTests : IClassFixture<SyncServerFactory>
{
    readonly SyncServerFactory _factory;

    public MigrationTests(SyncServerFactory factory) => _factory = factory;

    [Fact]
    public async Task El_esquema_se_crea_aplicando_migraciones()
    {
        await _factory.WithDbAsync(async db =>
        {
            var aplicadas = (await db.Database.GetAppliedMigrationsAsync()).ToList();

            Assert.NotEmpty(aplicadas);
            Assert.Contains(aplicadas, m => m.EndsWith("Inicial", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task No_quedan_migraciones_pendientes()
    {
        await _factory.WithDbAsync(async db =>
            Assert.Empty(await db.Database.GetPendingMigrationsAsync()));
    }

    [Fact]
    public async Task Volver_a_migrar_sobre_una_base_ya_migrada_no_rompe()
    {
        // es exactamente lo que pasa en cada reinicio del server y en cada actualización
        await _factory.WithDbAsync(async db => await db.Database.MigrateAsync());
        await _factory.WithDbAsync(async db =>
            Assert.Empty(await db.Database.GetPendingMigrationsAsync()));
    }

    [Fact]
    public void Los_dos_proveedores_tienen_su_assembly_de_migraciones()
    {
        var sqlite = new DbContextOptionsBuilder<SyncDbContext>();
        var postgres = new DbContextOptionsBuilder<SyncDbContext>();

        DatabaseSetup.Configure(sqlite, "sqlite", "Data Source=:memory:");
        DatabaseSetup.Configure(postgres, "postgres", "Host=localhost;Database=x");

        Assert.Equal(DatabaseSetup.SqliteMigrations, MigrationsAssemblyOf(sqlite));
        Assert.Equal(DatabaseSetup.PostgresMigrations, MigrationsAssemblyOf(postgres));
    }

    static string? MigrationsAssemblyOf(DbContextOptionsBuilder builder) =>
        builder.Options.Extensions
            .OfType<Microsoft.EntityFrameworkCore.Infrastructure.RelationalOptionsExtension>()
            .Select(e => e.MigrationsAssembly)
            .FirstOrDefault(a => a != null);
}
