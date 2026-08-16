# Spike de Android

Un head de Android mínimo sobre el mismo Core, para responder con datos si vale la pena llevar
EasyRest al teléfono. **No es un producto**: es una pantalla de diagnóstico que ejercita las dos
piezas que estaban en duda —el motor de scripts y el almacenamiento— además de mandar una request
de verdad.

## Bajar el APK

El job `android-spike` arma el APK en cada corrida y lo deja como artefacto: *Actions → la
corrida → Artifacts → `EasyRest-android-spike-apk`*. Viene firmado con el keystore de depuración,
así que se instala en cualquier teléfono con "orígenes desconocidos" habilitado, sin necesidad de
un keystore propio.

Se compila en **Release** aunque sea un spike: en Debug, .NET Android usa fast deployment y deja
los assemblies fuera del APK, pensando en que el IDE los va a empujar por adb — un APK Debug
instalado a mano no arranca. El trimming queda en el default de Release (`TrimMode=partial`, sólo
toca los assemblies del SDK), que es la configuración que `tests/aot-probe` ya verificó con Jint.

El job además corre `aapt2 dump badging` y falla si el APK no declara una actividad de lanzador
o no trae ícono: sin esa verificación, la app instala, no aparece en el cajón de aplicaciones y
el log del build no muestra nada raro.

```bash
adb install com.rentlysoft.easyrest-Signed.apk
```

### El APK que instalaba sin aparecer en el cajón

Vale la pena dejarlo escrito porque costó encontrarlo y porque **cualquier head de Android con
Avalonia se lo puede comer**.

El síntoma: el APK instalaba, figuraba en Ajustes y no tenía ni ícono ni entrada en el lanzador.
El build terminaba con cero warnings.

La causa: **el assembly de la app nunca entraba al pipeline de Android**. `GenerateJavaStubs`
recibía 245 assemblies y `EasyRest.Android.dll` no estaba entre ellos; tampoco se copiaba a
`android/assets/`. Sin ese assembly no se genera el wrapper de Java de `MainActivity`, y sin
wrapper no hay `<activity>` en el manifiesto. Los tipos de `Avalonia.Android` y `Mono.Android`
sí generaban sus wrappers, que es lo que hacía parecer que el paso funcionaba.

Y la causa de la causa está en los targets de Avalonia:

```xml
<Target Name="PrepareToCompileAvaloniaXaml">
  <IntermediateAssembly Update="*" AvaloniaCompileOutput="%(RelativeDir)Avalonia\%(Filename)%(Extension)"/>

<Target Name="InjectAvaloniaXamlOutput" DependsOnTargets="PrepareToCompileAvaloniaXaml" …>
  <IntermediateAssembly Remove="@(IntermediateAssembly)"/>
  <IntermediateAssembly Include="@(_AvaloniaXamlCompiledAssembly)"/>
```

El compilador de XAML reescribe el assembly hacia `obj/…/Avalonia/`, calculando la ruta desde el
`%(RelativeDir)` del propio item y después reemplazándolo. En el build de Android eso se aplica
dos veces, y la segunda vuelta produce `obj/…/Avalonia/Avalonia/EasyRest.Android.dll`, que no
existe. Con el trimming activado el error se ve:

```
error IL1032: Root assembly 'obj/Release/net8.0-android34.0/Avalonia/Avalonia/EasyRest.Android.dll'
              could not be found.
```

Con el linker apagado —que era la configuración original, primero como `AndroidLinkMode=None` y
después como `PublishTrimmed=false`— nadie se quejaba: el assembly simplemente desaparecía y el
APK salía mudo.

Por eso hoy el head **no usa XAML**: la UI está armada en C#. El guardia de esos dos targets es
`'@(AvaloniaResource)@(AvaloniaXaml)' != ''`, así que sin archivos `.axaml` no corren y el
assembly queda donde el SDK de Android lo espera. Es una decisión del spike, no una recomendación
general: **si el móvil avanza a producto, esto hay que resolverlo de verdad** (arreglo aguas
arriba en Avalonia, o fijar la ruta del assembly intermedio), porque una app de verdad va a
querer XAML.

De paso quedaron dos cosas más, que eran mejoras reales aunque no fueran la causa: se arma con
`dotnet publish` (el camino soportado para un APK de Release) y el trimming vuelve al default de
Release.

### Si dice "aplicación no instalada"

Desinstalá la versión anterior primero:

```bash
adb uninstall com.rentlysoft.easyrest
```

No es un bug del APK: sin un keystore propio, .NET Android firma con una clave de depuración que
**se genera por máquina**, y cada corrida del CI es una máquina nueva. Mismo `applicationId` con
firma distinta es una actualización que Android rechaza. Pasa entre APKs de corridas distintas, y
seguirá pasando hasta que haya un keystore fijo (ver abajo).

## Correrlo desde el código

Hace falta el workload de Android una sola vez:

```bash
dotnet workload install android
```

Con un teléfono conectado por USB y depuración habilitada, o un emulador andando:

```bash
dotnet build src/EasyRest.Android/EasyRest.Android.csproj -t:Run
```

La pantalla muestra el sistema, la arquitectura, si el runtime puede generar código y si la
carpeta privada de la app se puede leer y escribir. Abajo hay una URL, un script post-respuesta
editable y un botón: al enviar corre el `HttpExecutor` del Core y después el script con Jint sobre
la respuesta, con sus asserts y el `console.log`.

## Lo que ya sabemos

### El Core corre sin JIT — verificado

Esto era la duda grande, porque **iOS no permite generar código en runtime**. Está verificado, no
supuesto: `tests/aot-probe` compila el Core con NativeAOT (sin JIT y sin Reflection.Emit, la misma
restricción) y ejercita scripts pre-request y post-respuesta, `JSON.parse`, `er.test`, `er.setVar`,
el resolver de variables, el export de ambientes y la generación de cURL. Los 15 chequeos pasan, y
corre en CI en cada PR.

```bash
dotnet publish tests/aot-probe -c Release -r linux-x64 -o /tmp/aot && /tmp/aot/AotProbe
```

### Pero es sensible al trimming, y eso cambia el plan de iOS

Con **trimming completo** los scripts se rompen: la interop de Jint resuelve por reflexión los
miembros de los objetos del host (`er.request.setHeader`, `er.response.body`) y el trimmer los
borra por no ver quién los usa. El síntoma es `Property 'setHeader' of object is not a function`
— no un error de compilación, sino una app que arranca bien y falla al correr un script.

La distinción importa: **el problema es el trimming, no la falta de JIT**. Con los metadatos
preservados (`TrimMode=partial`) todo funciona sin JIT.

Qué significa para cada plataforma:

- **Android**: usa Mono con JIT. Sin problema.
- **iOS**: usa Mono AOT, que prohíbe generar código pero **conserva la reflexión**, así que en
  principio está bien. Pero si alguna vez se activa el trimming agresivo para bajar el tamaño del
  `.ipa`, los scripts se rompen en silencio. Antes de prometer iOS hay que **probar los scripts en
  un dispositivo real** con la configuración de release final.
- **Escritorio**: hoy no se trimea. Si algún día se activa `PublishTrimmed` para bajar los 98 MB
  del publish, aplica la misma trampa.

Esto corrige lo que dije antes de medirlo: que Jint no use `Reflection.Emit` es cierto, pero no
alcanzaba para dar iOS por resuelto.

### Un bug encontrado y arreglado

`EnvironmentShare.ToJson` serializaba tipos anónimos, y System.Text.Json no puede construirles el
converter en un runtime AOT: la app crasheaba al compartir un ambiente. Se cambió por clases
propias, con el mismo formato de salida (hay un chequeo en la sonda que lo fija). Era un bloqueo
real para móvil y estaba escondido en el escritorio, donde el JIT lo tapaba.

## Lo que falta responder

El spike está para contestar esto **en un teléfono**, que es lo que no se puede hacer desde acá:

- [ ] ¿Cómo se siente Avalonia en móvil? Scroll, teclado en pantalla, tamaño de los toques.
- [ ] ¿Cuánto pesa el APK y cuánto tarda en abrir?
- [ ] ¿El teclado tapa el campo de la URL al escribir?
- [ ] ¿La request sale sin problemas con datos móviles, no sólo con WiFi?

## Decisiones que tomé y conviene revisar

- **No referencia a `EasyRest.Avalonia`**: esa app arrastra `Avalonia.Desktop` y una UI de mouse y
  teclado. El head sólo usa `EasyRest.Core`, que es lo que se quiere validar.
- **Avalonia 11.2 acá, 11.1.3 en el escritorio**: mobile recién se estabilizó en 11.2 y un spike
  no tiene por qué mover la versión de la app. Si el móvil avanza, lo primero es unificar.
- **La UI va en C# y no en XAML**, por el bug de build de más arriba. Para dos pantallas de
  diagnóstico no cuesta nada; para un producto sí, y ahí hay que resolver el bug.
- El `AndroidManifest.xml` pide **permiso de INTERNET**: sin eso `HttpClient` falla en silencio,
  que en un cliente HTTP sería gracioso.
- **Ícono propio**, generado por código (`Resources/mipmap-*`): dos flechas encontradas sobre el
  fondo de catppuccin que usa el escritorio. No es un logo, es para no quedarse con el androide
  genérico y poder distinguir la app en el cajón.
- **No está en `EasyRest.slnx`** a propósito: sin el workload instalado, tenerlo en la solución
  rompería el build para todos los demás. Se compila apuntándole al csproj, y en CI tiene su
  propio job.
- **Sin keystore propio**: cada corrida firma con una clave de depuración distinta, así que
  actualizar de un APK a otro obliga a desinstalar. Si el spike avanza y molesta, se arregla
  guardando un keystore como secret del repo y firmando siempre con ese — son diez líneas de
  workflow, pero no las agrego para un experimento.
