using System.Runtime.CompilerServices;
using EasyRest.Models;
using EasyRest.Services;

// Compilado con NativeAOT no hay JIT ni Reflection.Emit: la misma restricción que impone iOS.
// Si el motor de scripts funciona acá, funciona allá.

var fallos = 0;

void Check(string que, bool ok, string detalle = "")
{
    Console.WriteLine(ok ? $"  OK   {que}" : $"  FALLA {que} {detalle}");
    if (!ok) fallos++;
}

Console.WriteLine($"Generación dinámica de código: {RuntimeFeature.IsDynamicCodeSupported}" +
                  $" (compilada: {RuntimeFeature.IsDynamicCodeCompiled})");
Console.WriteLine();

Check("el runtime está sin JIT, como en iOS", !RuntimeFeature.IsDynamicCodeSupported);

// --- El motor de scripts, que es lo que estaba en duda ---

var env = new EnvironmentModel { Name = "Pruebas" };
env.Variables.Add(new KeyValueItem { Key = "base", Value = "https://api.example.com" });

var pre = ScriptRunner.Run(
    """
    er.request.url = er.getVar("base") + "/login";
    er.request.setHeader("X-Origen", "aot");
    er.setVar("intento", "1");
    console.log("pre-request corrió");
    """,
    env,
    new ScriptRequestProxy("GET", "https://placeholder", "", Array.Empty<(string, string)>()),
    null);

Check("el pre-request no tira error", pre.Error == null, pre.Error ?? "");
Check("resuelve variables y reescribe la URL", pre.Log.ToString().Contains("pre-request corrió"));
Check("setVar escribe en el ambiente",
    env.Variables.Any(v => v.Key == "intento" && v.Value == "1"));

var post = ScriptRunner.Run(
    """
    var cuerpo = JSON.parse(er.response.body);
    er.test("responde 200", er.response.status === 200);
    er.test("trae token", cuerpo.access_token.length > 0);
    er.test("esta falla a propósito", cuerpo.access_token === "otra-cosa");
    er.setVar("token", cuerpo.access_token);
    """,
    env,
    null,
    new ScriptResponseInfo(200, """{"access_token":"abc123"}""", new List<HeaderEntry>(), 12.5));

Check("el post-response no tira error", post.Error == null, post.Error ?? "");
Check("JSON.parse anda sin JIT", post.Tests.Count == 3);
Check("los asserts que pasan, pasan", post.Tests.Count(t => t.Passed) == 2);
Check("los asserts que fallan, fallan", post.Tests.Count(t => !t.Passed) == 1);
Check("extrae el token a una variable",
    env.Variables.Any(v => v.Key == "token" && v.Value == "abc123"));

// --- Lo demás del Core que la app móvil necesita ---

Check("resuelve {{variables}}",
    VariableResolver.Resolve("{{base}}/v1", env) == "https://api.example.com/v1");

var compartido = EnvironmentShare.ToJson(env, includeValues: false);
Check("comparte ambientes sin filtrar valores",
    compartido.Contains("\"base\"") && !compartido.Contains("api.example.com"));
// el formato compartido no puede cambiar por hacerlo compatible con AOT
Check("mantiene el formato compartido",
    compartido.Contains("\"easyrest\": \"environment\"") &&
    compartido.Contains("\"variables\"") && compartido.Contains("\"enabled\""),
    compartido.Replace("\n", " "));

var reimportado = EnvironmentShare.TryParse(EnvironmentShare.ToJson(env));
Check("lo que exporta lo puede volver a importar",
    reimportado != null && reimportado.Variables.Any(v => v.Key == "base"));

var curl = CurlHelper.ToCurl(new RequestItem { Name = "x", Method = "POST", Url = "https://api.example.com/x" },
    null, env);
Check("genera cURL", curl.Contains("curl") && curl.Contains("api.example.com"));

// AppData: en Android e iOS esto devuelve el directorio privado de la app
var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
Check("hay una carpeta de datos de la app", !string.IsNullOrEmpty(appData), appData);

Console.WriteLine();
Console.WriteLine(fallos == 0
    ? "Todo bien: el Core corre sin generación dinámica de código."
    : $"{fallos} chequeo(s) fallaron.");
return fallos == 0 ? 0 : 1;
