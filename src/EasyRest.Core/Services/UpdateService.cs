using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EasyRest.Services;

/// <summary>Qué hay que reemplazar para actualizar: el .exe autocontenido (Windows) o el
/// bundle .app (macOS). Cualquier otro caso (Linux, `dotnet run`) no es actualizable.</summary>
public enum InstallKind { WindowsExe, MacAppBundle }

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
    /// actual es reemplazable (exe autocontenido o .app).</summary>
    public bool CanInstall => !string.IsNullOrEmpty(AssetUrl) && UpdateService.InstallTarget != null;
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

    /// <summary>Sufijo del zip que publica el CI para esta plataforma (`EasyRest-&lt;sufijo&gt;.zip`),
    /// o null si no se publican binarios para ella (Linux).</summary>
    public static string? AssetSuffix
    {
        get
        {
            if (OperatingSystem.IsWindows()) return "windows-x64";
            if (OperatingSystem.IsMacOS())
                return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "macos-arm64" : "macos-x64";
            return null;
        }
    }

    /// <summary>Qué instalación se puede reemplazar en caliente, o null si no aplica (Linux,
    /// `dotnet run`, o un layout inesperado).</summary>
    public static InstallTargetInfo? InstallTarget
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;

            if (OperatingSystem.IsWindows())
                return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? new InstallTargetInfo(InstallKind.WindowsExe, exe)
                    : null;

            if (OperatingSystem.IsMacOS())
            {
                // .../EasyRest.app/Contents/MacOS/EasyRest → .../EasyRest.app
                var i = exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                return i > 0 ? new InstallTargetInfo(InstallKind.MacAppBundle, exe[..(i + 4)]) : null;
            }

            return null;
        }
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
        var suffix = AssetSuffix;
        if (suffix != null && root.TryGetProperty("assets", out var assets) &&
            assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name");
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !name.Contains(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                assetName = name;
                assetUrl = Str(asset, "browser_download_url");
                assetSize = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var v) ? v : 0;
                break;
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

        EnsureWritable(target.Path);

        if (target.Kind == InstallKind.WindowsExe) ApplyWindows(zipPath, target.Path);
        else ApplyMac(zipPath, target.Path);
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

    static void ApplyWindows(string zipPath, string exePath)
    {
        var work = Path.GetDirectoryName(zipPath)!;
        var extracted = Path.Combine(work, "new");
        ZipFile.ExtractToDirectory(zipPath, extracted, overwriteFiles: true);

        var newExe = Directory.GetFiles(extracted, "*.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("El zip descargado no trae EasyRest.exe.");

        // el move falla mientras el exe siga en uso, así que reintentar es la forma de esperar
        // a que la app cierre (sin bloques con paréntesis: ahí %var% se expandiría una sola vez)
        var script = Path.Combine(work, "apply-update.cmd");
        File.WriteAllText(script, string.Join("\r\n", new[]
        {
            "@echo off",
            "setlocal",
            "set /a tries=0",
            ":retry",
            $"move /y \"{newExe}\" \"{exePath}\" >nul 2>&1",
            "if not errorlevel 1 goto done",
            "set /a tries+=1",
            "if %tries% geq 90 goto fail",
            "ping -n 2 127.0.0.1 >nul",
            "goto retry",
            ":done",
            $"start \"\" \"{exePath}\"",
            $"del /q \"{zipPath}\" >nul 2>&1",
            $"rmdir /s /q \"{extracted}\" >nul 2>&1",
            "exit /b 0",
            ":fail",
            "exit /b 1",
            ""
        }));

        StartDetached("cmd.exe", new[] { "/c", script }, work);
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
