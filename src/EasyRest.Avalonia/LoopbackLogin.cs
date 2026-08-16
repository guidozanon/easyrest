using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyRest.Services.Sync;

namespace EasyRest.Avalonia;

/// <summary>El login OAuth de la app de escritorio: abre el navegador del sistema y espera el
/// código en un servidor mínimo sobre 127.0.0.1.
///
/// Va acá y no en el Core porque HttpListener es una solución de escritorio: en el teléfono el
/// redirect vuelve por un esquema propio registrado en el sistema, no por un puerto local.
///
/// El navegador del sistema y no uno embebido: así el login usa las sesiones y el gestor de
/// contraseñas que la persona ya tiene, y la app nunca ve la contraseña.</summary>
public static class LoopbackLogin
{
    /// <summary>Corre el flujo completo y devuelve la sesión. Cancelar cierra el escucha.</summary>
    public static async Task<SyncSession> RunAsync(SyncApiClient api, string providerId,
        CancellationToken ct = default)
    {
        var pkce = SyncPkce.Create();
        using var escucha = new HttpListener();
        var redirectUri = Reservar(escucha);

        escucha.Start();
        try
        {
            var url = api.BuildLoginUrl(providerId, redirectUri, pkce.Challenge, pkce.State);
            AbrirNavegador(url);

            var (code, state, error) = await EsperarRespuestaAsync(escucha, ct);

            if (error != null)
                throw new SyncApiException($"El login falló: {error}", HttpStatusCode.Unauthorized);
            // sin esta comparación, cualquier página abierta podría empujar un código ajeno
            if (state != pkce.State)
                throw new SyncApiException("La respuesta del navegador no corresponde a este login.",
                    HttpStatusCode.BadRequest);
            if (code == null)
                throw new SyncApiException("El navegador volvió sin código.", HttpStatusCode.BadRequest);

            return await api.ExchangeCodeAsync(code, pkce.Verifier, ct);
        }
        finally
        {
            if (escucha.IsListening) escucha.Stop();
        }
    }

    /// <summary>Toma un puerto libre. Se prueba y se descarta hasta que uno entra: pedirle al SO
    /// "cualquiera" no sirve, porque HttpListener necesita el prefijo completo de antemano.</summary>
    static string Reservar(HttpListener escucha)
    {
        var rnd = new Random();
        for (var intento = 0; intento < 20; intento++)
        {
            var puerto = rnd.Next(49152, 65535);
            var prefijo = $"http://127.0.0.1:{puerto}/easyrest-login/";
            try
            {
                escucha.Prefixes.Clear();
                escucha.Prefixes.Add(prefijo);
                escucha.Start();
                escucha.Stop();
                return prefijo;
            }
            catch (HttpListenerException)
            {
                // puerto ocupado o sin permiso: se prueba otro
            }
        }
        throw new SyncApiException("No se encontró un puerto local libre para recibir el login.",
            HttpStatusCode.ServiceUnavailable);
    }

    static async Task<(string? Code, string? State, string? Error)> EsperarRespuestaAsync(
        HttpListener escucha, CancellationToken ct)
    {
        // GetContextAsync no acepta cancelación: se corta cerrando el escucha
        await using var _ = ct.Register(() => { if (escucha.IsListening) escucha.Stop(); });

        HttpListenerContext contexto;
        try
        {
            contexto = await escucha.GetContextAsync();
        }
        catch (HttpListenerException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        var query = contexto.Request.QueryString;
        var resultado = (query["code"], query["state"], query["error"]);

        await ResponderAsync(contexto, resultado.Item3 == null
            ? "Listo. Ya podés volver a EasyRest."
            : $"El login falló: {resultado.Item3}");

        return resultado;
    }

    static async Task ResponderAsync(HttpListenerContext contexto, string mensaje)
    {
        // Página mínima y autocontenida: la ve el navegador, no la app, y no tiene por qué pedir
        // nada a la red.
        var html = $"""
            <!doctype html><html lang="es"><meta charset="utf-8">
            <title>EasyRest</title>
            <body style="background:#1e1e2e;color:#cdd6f4;font:16px system-ui;display:grid;
                         place-items:center;height:100vh;margin:0">
              <p>{WebUtility.HtmlEncode(mensaje)}</p>
            </body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);

        contexto.Response.ContentType = "text/html; charset=utf-8";
        contexto.Response.ContentLength64 = bytes.Length;
        await contexto.Response.OutputStream.WriteAsync(bytes);
        contexto.Response.Close();
    }

    static void AbrirNavegador(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new SyncApiException(
                "No se pudo abrir el navegador. Copiá esta dirección a mano:\n" + url,
                HttpStatusCode.ServiceUnavailable);
        }
    }
}
