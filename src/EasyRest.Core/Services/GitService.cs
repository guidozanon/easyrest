using System.Diagnostics;
using System.IO;
using System.Text;

namespace EasyRest.Services;

public record GitStatusInfo(string Branch, int Pending, int Ahead, int Behind, string? Remote);

/// <summary>Wrapper del git CLI. La autenticación la resuelve el credential manager del sistema
/// (GIT_TERMINAL_PROMPT=0 evita que git se cuelgue pidiendo credenciales por consola).</summary>
public static class GitService
{
    static bool? _available;

    public static bool IsAvailable()
    {
        _available ??= Run("--version", null).Code == 0;
        return _available.Value;
    }

    public static (string? Name, string? Email) Identity(string workDir)
    {
        var name = Run("config user.name", workDir);
        var email = Run("config user.email", workDir);
        return (name.Code == 0 ? name.Out.Trim() : null, email.Code == 0 ? email.Out.Trim() : null);
    }

    public static bool IsRepo(string dir)
    {
        if (!Directory.Exists(dir)) return false;
        var r = Run("rev-parse --is-inside-work-tree", dir);
        return r.Code == 0 && r.Out.Trim() == "true";
    }

    public static (bool Ok, string Message) Init(string dir)
    {
        var r = Run("init -b main", dir);
        return r.Code == 0 ? (true, "Repositorio inicializado (rama main).") : (false, ErrorOf(r));
    }

    public static (bool Ok, string Message) Clone(string url, string dir)
    {
        Directory.CreateDirectory(dir);
        var r = Run($"clone {Quote(url)} .", dir, timeoutMs: 120_000);
        return r.Code == 0 ? (true, "Repositorio clonado.") : (false, ErrorOf(r));
    }

    public static string? GetRemote(string dir)
    {
        var r = Run("remote get-url origin", dir);
        return r.Code == 0 ? r.Out.Trim() : null;
    }

    public static (bool Ok, string Message) SetRemote(string dir, string url)
    {
        var r = GetRemote(dir) == null
            ? Run($"remote add origin {Quote(url)}", dir)
            : Run($"remote set-url origin {Quote(url)}", dir);
        return r.Code == 0 ? (true, "Remote configurado.") : (false, ErrorOf(r));
    }

    public static GitStatusInfo? Status(string dir)
    {
        if (!IsRepo(dir)) return null;

        var branch = Run("rev-parse --abbrev-ref HEAD", dir);
        var branchName = branch.Code == 0 ? branch.Out.Trim() : "?";

        var porcelain = Run("status --porcelain", dir);
        var pending = porcelain.Code == 0
            ? porcelain.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;

        int ahead = 0, behind = 0;
        var counts = Run("rev-list --left-right --count HEAD...@{upstream}", dir);
        if (counts.Code == 0)
        {
            var parts = counts.Out.Trim().Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out ahead);
                int.TryParse(parts[1], out behind);
            }
        }

        return new GitStatusInfo(branchName, pending, ahead, behind, GetRemote(dir));
    }

    /// <summary>add -A → commit si hay cambios → pull --rebase → push. Si el rebase da conflictos,
    /// lo aborta y avisa (la resolución de conflictos queda para tu cliente git).</summary>
    public static (bool Ok, string Message) Sync(string dir)
    {
        if (!IsRepo(dir)) return (false, "La carpeta del workspace no es un repositorio git.");

        var log = new StringBuilder();

        var add = Run("add -A", dir);
        if (add.Code != 0) return (false, ErrorOf(add));

        var porcelain = Run("status --porcelain", dir);
        var hasChanges = porcelain.Code == 0 && porcelain.Out.Trim().Length > 0;
        if (hasChanges)
        {
            var commit = Run($"commit -m {Quote($"EasyRest sync {DateTime.Now:yyyy-MM-dd HH:mm}")}", dir);
            if (commit.Code != 0)
            {
                var (name, email) = Identity(dir);
                if (name == null || email == null)
                    return (false, "No se pudo commitear: falta configurar user.name / user.email de git.");
                return (false, ErrorOf(commit));
            }
            log.AppendLine("✔ Cambios commiteados.");
        }
        else
        {
            log.AppendLine("Sin cambios locales para commitear.");
        }

        if (GetRemote(dir) == null)
        {
            log.AppendLine("Sin remote configurado: el commit quedó local.");
            return (true, log.ToString().Trim());
        }

        // sin upstream (primer push, o remote recién configurado) no hay de dónde pullear
        var hasUpstream = Run("rev-parse --abbrev-ref @{upstream}", dir).Code == 0;
        if (hasUpstream)
        {
            var pull = Run("pull --rebase", dir, timeoutMs: 120_000);
            if (pull.Code != 0)
            {
                Run("rebase --abort", dir);
                return (false, "El pull --rebase falló (posibles conflictos). Se abortó el rebase: " +
                               "resolvé los conflictos con tu cliente git y volvé a sincronizar.\n" + ErrorOf(pull));
            }
            log.AppendLine("✔ Pull (rebase) OK.");
        }
        else
        {
            log.AppendLine("Primera sincronización con el remote (sin upstream todavía).");
        }

        var push = Run("push -u origin HEAD", dir, timeoutMs: 120_000);
        if (push.Code != 0) return (false, "El push falló:\n" + ErrorOf(push));
        log.AppendLine("✔ Push OK.");

        return (true, log.ToString().Trim());
    }

    // ----- Infraestructura -----

    static (int Code, string Out, string Err) Run(string args, string? workDir, int timeoutMs = 30_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workDir ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";

            using var process = Process.Start(psi);
            if (process == null) return (-1, "", "No se pudo iniciar git.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ya salió */ }
                return (-1, "", $"git {args} superó el tiempo de espera.");
            }
            return (process.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    static string ErrorOf((int Code, string Out, string Err) r) =>
        string.IsNullOrWhiteSpace(r.Err) ? r.Out.Trim() : r.Err.Trim();

    static string Quote(string s) => $"\"{s.Replace("\"", "\\\"")}\"";
}
