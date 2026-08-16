using System.Diagnostics;
using System.IO.Compression;
using EasyRest.Services;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

/// <summary>El contrato entre cómo empaqueta el CI y cómo desempaqueta el auto update: el CI
/// arma el tar.gz con `tar` y el cliente lo abre con System.Formats.Tar, así que conviene
/// verificar el ida y vuelta completo y no sólo confiar en que los formatos coinciden.</summary>
public class UpdatePackagingTests : IDisposable
{
    readonly List<string> _temp = new();

    [Fact]
    public void El_targz_conserva_el_bit_de_ejecucion()
    {
        if (OperatingSystem.IsWindows()) return;   // los permisos Unix no existen en Windows

        var payload = NewDir();
        var app = Path.Combine(payload, "EasyRest");
        File.WriteAllText(app, "#!/bin/sh\n");
        File.SetUnixFileMode(app, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                                  UnixFileMode.UserExecute | UnixFileMode.GroupRead |
                                  UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                                  UnixFileMode.OtherExecute);
        File.WriteAllText(Path.Combine(payload, "EasyRest.dll"), "x");

        // se empaqueta igual que en el workflow: tar -czf ... -C stage EasyRest
        var archive = Path.Combine(NewDir(), "EasyRest-linux-x64.tar.gz");
        Run("tar", "-czf", archive, "-C", Path.GetDirectoryName(payload)!, Path.GetFileName(payload));

        var target = NewDir();
        UpdateService.Extract(archive, target);

        var extracted = Path.Combine(target, Path.GetFileName(payload), "EasyRest");
        Assert.True(File.Exists(extracted));
        Assert.True(File.GetUnixFileMode(extracted).HasFlag(UnixFileMode.UserExecute),
            "el binario extraído tiene que seguir siendo ejecutable");
    }

    [Fact]
    public void El_zip_sigue_sirviendo_para_windows_y_mac()
    {
        var payload = NewDir();
        File.WriteAllText(Path.Combine(payload, "EasyRest.exe"), "x");
        var archive = Path.Combine(NewDir(), "EasyRest-win-x64-portable.zip");
        ZipFile.CreateFromDirectory(payload, archive);

        var target = NewDir();
        UpdateService.Extract(archive, target);

        Assert.True(File.Exists(Path.Combine(target, "EasyRest.exe")));
    }

    string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"easyrest-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _temp.Add(dir);
        return dir;
    }

    static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
        if (p.ExitCode != 0) throw new InvalidOperationException(p.StandardError.ReadToEnd());
    }

    public void Dispose()
    {
        foreach (var dir in _temp.Where(Directory.Exists)) Directory.Delete(dir, recursive: true);
    }
}
