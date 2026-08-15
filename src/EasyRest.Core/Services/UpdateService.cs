using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EasyRest.Services;

/// <summary>Qué hay que reemplazar para actualizar: la carpeta de instalación (Windows y Linux),
/// el bundle .app (macOS), o un .exe single-file de las versiones ≤0.1.10 (que ya no se publican
/// y hay que reinstalar a mano). Correr desde el código (`dotnet run`) no es actualizable.</summary>
public enum InstallKind { WindowsFolder, WindowsLegacySingleFile, MacAppBundle, LinuxFolder }

public record InstallTargetInfo(InstallKind Kind, string Path);

/// <summary>Una release de GitHub comparada con la versión instalada. AssetUrl es el zip de esta
/// plataforma (null si la release no trae binario para ella).</summary>
public record UpdateInfo(
    string Version,
    string CurrentVersion,
    string Notes,
    string ReleaseUrl,
    string? AssetName,
    string? AssetUrl,
    long AssetSize)
{
    /// <summary>Hay una versión más nueva que la instalada.</summary>
    public bool IsNewer => UpdateService.IsNewer(Version, CurrentVersion);

    /// <summary>Se puede instalar sola: hay binario para esta plataforma y la instalación
    /// actual es reemplazable (carpeta de Windows o .app).</summary>
    public bool CanInstall => !string.IsNullOrEmpty(AssetUrl) &&
                              UpdateService.InstallTarget is { Kind: not InstallKind.WindowsLegacySingleFile };
}

/// <summary>Auto-update contra los Releases de GitHub: consulta la última versión, baja el zip de
/// la plataforma y lo aplica con un script externo que espera a que la app cierre, reemplaza los
/// binarios y vuelve a abrir EasyRest.</summary>
public static class UpdateService
{
    public const string RepoUrl = "https://github.com/guidozanon/easyrest";
    const string LatestReleaseApi = "https://api.github.com/repos/guidozanon/easyrest/releases/latest";

    /// <summary>Versión instalada. La inyecta el CI con -p:Version=&lt;tag&gt; al publicar;
    /// en dev queda la del csproj.</summary>
    public static string CurrentVersion { get; } = ReadCurrentVersion();

    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(15)  // el zip puede pesar decenas de MB
        };
        // la API de GitHub rechaza las requests sin User-Agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyRest-Updater/" + CurrentVersion);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    static string ReadCurrentVersion()
    {
        // la app es el entry assembly (Core puede tener otra versión)
        var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info!.IndexOf('+');
            return (plus > 0 ? info[..plus] : info).Trim();
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>Etiqueta de plataforma de los assets del CI, o null si no se publican binarios
    /// para ella (Linux). La UI la usa para distinguir "no hay binarios" de "no se puede
    /// reemplazar esta instalación".</summary>
    public static string? AssetSuffix
    {
        get
        {
            var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            if (OperatingSystem.IsWindows()) return "windows-x64";
            if (OperatingSystem.IsMacOS()) return arm ? "macos-arm64" : "macos-x64";
            if (OperatingSystem.IsLinux()) return arm ? "linux-arm64" : "linux-x64";
            return null;
        }
    }

    /// <summary>Zip portable de Windows: la carpeta autocontenida completa. A propósito no dice
    /// "windows-x64", porque las versiones ≤0.1.10 tomaban cualquier zip que tuviera ese texto y
    /// sabían reemplazar un único .exe: si les matcheara esto, se romperían solas.</summary>
    const string WindowsPortableAsset = "EasyRest-win-x64-portable.zip";

    /// <summary>Qué instalación se puede reemplazar en caliente, o null si no aplica (Linux,
    /// `dotnet run`, o un layout inesperado).</summary>
    public static InstallTargetInfo? InstallTarget
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;

            if (OperatingSystem.IsWindows())
            {
                if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
                var dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir)) return null;

                // el publish en carpeta deja el assembly al lado del exe; en el single-file de
                // ≤0.1.10 está embebido y no hay nada más que el .exe
                if (!File.Exists(Path.Combine(dir, "EasyRest.dll")))
                    return new InstallTargetInfo(InstallKind.WindowsLegacySingleFile, exe);

                // el host nativo sólo aparece en un publish autocontenido: si no está, esto es
                // un `dotnet run` sobre bin\Debug y no hay que pisarlo
                if (!File.Exists(Path.Combine(dir, "hostpolicy.dll"))) return null;

                return new InstallTargetInfo(InstallKind.WindowsFolder, dir);
            }

            if (OperatingSystem.IsMacOS())
            {
                // .../EasyRest.app/Contents/MacOS/EasyRest → .../EasyRest.app
                var i = exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                return i > 0 ? new InstallTargetInfo(InstallKind.MacAppBundle, exe[..(i + 4)]) : null;
            }

            if (OperatingSystem.IsLinux())
            {
                var dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir)) return null;

                // mismo criterio que en Windows: el host nativo sólo está en un publish
                // autocontenido, así que un `dotnet run` sobre bin/Debug no se toca
                if (!File.Exists(Path.Combine(dir, "EasyRest.dll")) ||
                    !File.Exists(Path.Combine(dir, "libhostpolicy.so"))) return null;

                return new InstallTargetInfo(InstallKind.LinuxFolder, dir);
            }

            return null;
        }
    }

    /// <summary>Nombres de asset que sirven para esta instalación, en orden de preferencia.
    /// Vacío si no hay ninguno aplicable (Linux, o un single-file viejo: esos no tienen asset
    /// y se reinstalan a mano con el installer).</summary>
    static IEnumerable<string> AssetCandidates()
    {
        var suffix = AssetSuffix;
        if (suffix == null) yield break;

        if (OperatingSystem.IsWindows())
        {
            if (InstallTarget?.Kind == InstallKind.WindowsLegacySingleFile) yield break;
            yield return WindowsPortableAsset;
            yield break;
        }

        // en Linux el CI publica tar.gz, que es la convención de la plataforma y lleva permisos
        // y symlinks de forma portable entre cualquier herramienta
        if (OperatingSystem.IsLinux())
        {
            yield return $"EasyRest-{suffix}.tar.gz";
            yield break;
        }

        yield return $"EasyRest-{suffix}.zip";
    }

    // ----- Chequeo -----

    /// <summary>Consulta la última release publicada. Tira excepción si no hay red o GitHub
    /// responde mal (el caller decide si lo muestra o lo ignora).</summary>
    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));  // el chequeo no puede colgar el arranque

        using var resp = await Client.GetAsync(LatestReleaseApi, cts.Token);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(cts.Token);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var version = Str(root, "tag_name").Trim().TrimStart('v', 'V');
        var releaseUrl = Str(root, "html_url");
        if (string.IsNullOrWhiteSpace(releaseUrl)) releaseUrl = RepoUrl + "/releases/latest";

        string? assetName = null, assetUrl = null;
        long assetSize = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in AssetCandidates())
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Str(asset, "name");
                    if (!name.Equals(candidate, StringComparison.OrdinalIgnoreCase)) continue;
                    assetName = name;
                    assetUrl = Str(asset, "browser_download_url");
                    assetSize = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var v) ? v : 0;
                    break;
                }
                if (assetUrl != null) break;
            }
        }

        return new UpdateInfo(version, CurrentVersion, Str(root, "body").Trim(),
            releaseUrl, assetName, assetUrl, assetSize);
    }

    static string Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Compara versiones tipo "0.1.7" (tolera el prefijo "v" y sufijos -beta/+build).
    /// Si alguna no parsea, cae a comparar los textos.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        var a = ParseVersion(candidate);
        var b = ParseVersion(current);
        if (a != null && b != null) return a > b;
        return !string.IsNullOrWhiteSpace(candidate) &&
               !string.Equals(candidate.Trim(), current?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static Version? ParseVersion(string? raw)
    {
        var s = (raw ?? "").Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(new[] { '-', '+' });   // 1.2.3-beta.1 / 1.2.3+abc
        if (cut > 0) s = s[..cut];
        if (s.Length == 0) return null;
        if (!s.Contains('.')) s += ".0";              // Version necesita major.minor
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    // ----- Descarga -----

    /// <summary>Baja el zip de la release a una carpeta temporal y devuelve su ruta.
    /// progress reporta 0..1 (si el server manda Content-Length).</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.AssetUrl) || string.IsNullOrEmpty(info.AssetName))
            throw new InvalidOperationException(
                "La release no publica binarios para esta plataforma: descargala desde GitHub.");

        var dir = Path.Combine(Path.GetTempPath(), "EasyRest-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, info.AssetName!);

        using (var resp = await Client.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? info.AssetSize;

            await using var source = await resp.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(file);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report(Math.Min(1d, (double)done / total));
            }
        }

        progress?.Report(1d);
        return file;
    }

    // ----- Instalación -----

    /// <summary>Deja la nueva versión desempaquetada y lanza un script externo que espera a que
    /// la app cierre, reemplaza los binarios y vuelve a abrir EasyRest. El caller tiene que cerrar
    /// la app inmediatamente después de que esto vuelva.</summary>
    public static void ApplyAndRestart(string zipPath)
    {
        var target = InstallTarget ?? throw new InvalidOperationException(
            "No se pudo determinar la instalación a reemplazar: actualizá a mano desde GitHub.");

        if (target.Kind == InstallKind.WindowsLegacySingleFile)
            throw new InvalidOperationException(
                "Esta instalación es la vieja de un único .exe y ya no se actualiza sola.\n" +
                "Bajá el installer (EasyRest-Setup) desde GitHub: es una sola vez, después vuelve " +
                "a actualizarse automáticamente.");

        EnsureWritable(target.Path);

        switch (target.Kind)
        {
            case InstallKind.WindowsFolder: ApplyWindowsFolder(zipPath, target.Path); break;
            case InstallKind.LinuxFolder: ApplyLinuxFolder(zipPath, target.Path); break;
            default: ApplyMac(zipPath, target.Path); break;
        }
    }

    /// <summary>Descomprime el asset descargado: tar.gz en Linux, zip en el resto.</summary>
    internal static void Extract(string archivePath, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var file = File.OpenRead(archivePath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, targetDir, overwriteFiles: true);
            return;
        }

        ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
    }

    /// <summary>Falla temprano si no hay permisos donde está instalada la app (Program Files sin
    /// admin, /Applications de otro usuario): mejor avisar que cerrar la app y no poder volver.</summary>
    static void EnsureWritable(string installPath)
    {
        var dir = Path.GetDirectoryName(installPath.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(dir)) return;
        var probe = Path.Combine(dir, ".easyrest-update-probe");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"No hay permisos de escritura en {dir}.\nInstalá la actualización a mano desde GitHub.", ex);
        }
    }

    static void ApplyWindowsFolder(string zipPath, string installDir)
    {
        installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        var parent = Path.GetDirectoryName(installDir) ?? throw new InvalidOperationException(
            $"No se pudo resolver la carpeta que contiene {installDir}.");

        // se extrae al lado de la instalación, no en %TEMP%: así el swap final es un rename en
        // el mismo volumen (`move` no sabe mover carpetas entre volúmenes)
        var staging = Path.Combine(parent, ".easyrest-update-" + Guid.NewGuid().ToString("N")[..8]);
        Extract(zipPath, staging);

        var newDir = FindAppDir(staging, "EasyRest.exe")
            ?? throw new InvalidOperationException("El zip descargado no trae EasyRest.exe.");

        var old = installDir + ".old";
        var exePath = Path.Combine(installDir, "EasyRest.exe");
        var work = Path.GetDirectoryName(zipPath)!;   // fuera de installDir: si no, no se puede renombrar

        // el rename falla mientras la app siga abierta, así que reintentar es la forma de
        // esperar a que cierre (sin bloques con paréntesis: ahí %var% se expandiría una sola vez)
        var script = Path.Combine(work, "apply-update.cmd");
        File.WriteAllText(script, string.Join("\r\n", new[]
        {
            "@echo off",
            "setlocal",
            "set /a tries=0",
            ":retry",
            $"rmdir /s /q \"{old}\" >nul 2>&1",
            $"move /y \"{installDir}\" \"{old}\" >nul 2>&1",
            "if not errorlevel 1 goto swap",
            "set /a tries+=1",
            "if %tries% geq 90 goto fail",
            "ping -n 2 127.0.0.1 >nul",
            "goto retry",
            ":swap",
            $"move /y \"{newDir}\" \"{installDir}\" >nul 2>&1",
            "if errorlevel 1 goto restore",
            // el desinstalador lo genera el installer y no viene en el zip: si no se rescata,
            // la entrada de "Agregar o quitar programas" queda apuntando a la nada
            $"copy /y \"{old}\\unins000.*\" \"{installDir}\\\" >nul 2>&1",
            $"start \"\" \"{exePath}\"",
            $"rmdir /s /q \"{old}\" >nul 2>&1",
            $"rmdir /s /q \"{staging}\" >nul 2>&1",
            $"del /q \"{zipPath}\" >nul 2>&1",
            "exit /b 0",
            // si el swap falla, la instalación vieja vuelve a su lugar: nunca quedarse sin app
            ":restore",
            $"move /y \"{old}\" \"{installDir}\" >nul 2>&1",
            $"start \"\" \"{exePath}\"",
            $"rmdir /s /q \"{staging}\" >nul 2>&1",
            "exit /b 1",
            ":fail",
            $"rmdir /s /q \"{staging}\" >nul 2>&1",
            "exit /b 1",
            ""
        }));

        StartDetached("cmd.exe", new[] { "/c", script }, work);
    }

    /// <summary>La carpeta con el ejecutable dentro de lo extraído (los paquetes traen todo bajo
    /// `EasyRest/`).</summary>
    static string? FindAppDir(string root, string executable)
    {
        if (File.Exists(Path.Combine(root, executable))) return root;
        return Directory.EnumerateDirectories(root)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, executable)));
    }

    static void ApplyLinuxFolder(string archivePath, string installDir)
    {
        installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        var parent = Path.GetDirectoryName(installDir) ?? throw new InvalidOperationException(
            $"No se pudo resolver la carpeta que contiene {installDir}.");

        var staging = Path.Combine(parent, ".easyrest-update-" + Guid.NewGuid().ToString("N")[..8]);
        Extract(archivePath, staging);

        var newDir = FindAppDir(staging, "EasyRest")
            ?? throw new InvalidOperationException("El paquete descargado no trae el ejecutable.");

        var work = Path.GetDirectoryName(archivePath)!;
        var script = Path.Combine(work, "apply-update.sh");
        var exePath = Path.Combine(installDir, "EasyRest");

        // A diferencia de Windows, acá se puede renombrar una carpeta con un binario en uso, así
        // que no hace falta reintentar: alcanza con esperar a que el proceso termine para no
        // levantar dos instancias.
        File.WriteAllText(script, string.Join("\n", new[]
        {
            "#!/bin/sh",
            $"pid={Environment.ProcessId}",
            "i=0",
            "while kill -0 \"$pid\" 2>/dev/null && [ $i -lt 90 ]; do sleep 1; i=$((i+1)); done",
            $"install={Quote(installDir)}",
            $"new={Quote(newDir)}",
            $"staging={Quote(staging)}",
            $"archive={Quote(archivePath)}",
            "old=\"$install.old\"",
            "rm -rf \"$old\"",
            "mv \"$install\" \"$old\" || exit 1",
            // si el swap falla, la instalación anterior vuelve a su lugar
            "mv \"$new\" \"$install\" || { mv \"$old\" \"$install\"; exit 1; }",
            "rm -rf \"$old\"",
            $"chmod +x {Quote(exePath)} 2>/dev/null",
            $"nohup {Quote(exePath)} >/dev/null 2>&1 &",
            "rm -f \"$archive\"",
            "rm -rf \"$staging\"",
            ""
        }));
        RunAndWait("/bin/chmod", new[] { "+x", script });

        StartDetached("/bin/sh", new[] { script }, work);
    }

    static void ApplyMac(string zipPath, string appPath)
    {
        var work = Path.GetDirectoryName(zipPath)!;
        var extracted = Path.Combine(work, "new");
        Directory.CreateDirectory(extracted);

        // ditto preserva el bundle, los permisos y la firma ad-hoc (un unzip común los pierde
        // y Gatekeeper mata la app)
        if (RunAndWait("/usr/bin/ditto", new[] { "-x", "-k", zipPath, extracted }) != 0)
            throw new InvalidOperationException("No se pudo descomprimir la actualización (ditto).");

        var newApp = Directory.GetDirectories(extracted, "*.app").FirstOrDefault()
            ?? throw new InvalidOperationException("El zip descargado no trae EasyRest.app.");

        var script = Path.Combine(work, "apply-update.sh");
        File.WriteAllText(script, string.Join("\n", new[]
        {
            "#!/bin/sh",
            "# espera a que EasyRest cierre, reemplaza el bundle y lo vuelve a abrir",
            $"pid={Environment.ProcessId}",
            "i=0",
            "while kill -0 \"$pid\" 2>/dev/null && [ $i -lt 90 ]; do sleep 1; i=$((i+1)); done",
            $"app={Quote(appPath)}",
            $"new={Quote(newApp)}",
            $"zip={Quote(zipPath)}",
            "rm -rf \"$app.old\"",
            "mv \"$app\" \"$app.old\" || exit 1",
            "mv \"$new\" \"$app\" || { mv \"$app.old\" \"$app\"; exit 1; }",
            "rm -rf \"$app.old\"",
            // al bajar el zip con HttpClient no queda cuarentena, pero por si acaso
            "xattr -dr com.apple.quarantine \"$app\" 2>/dev/null",
            "open \"$app\"",
            "rm -f \"$zip\"",
            ""
        }));
        RunAndWait("/bin/chmod", new[] { "+x", script });

        StartDetached("/bin/sh", new[] { script }, work);
    }

    /// <summary>Comilla simple para sh ('...' con el escape clásico de la comilla).</summary>
    static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    static void StartDetached(string file, string[] args, string workDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    static int RunAndWait(string file, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p == null) return -1;
        p.WaitForExit(120_000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
