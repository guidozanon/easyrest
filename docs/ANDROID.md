# Android

El head de Android sobre el mismo Core. Empezó como un spike de diagnóstico y hoy tiene
colecciones, editor de request y sincronización con el servidor de sync.

La app abre en la lista de colecciones. Tocar una request abre el editor, que manda de verdad con
el `HttpExecutor` del Core y corre los scripts con Jint. La pantalla de diagnóstico del spike sigue
disponible desde el botón «Diagnóstico».

**El editor edita y guarda.** Método, URL, query params, cabeceras, autenticación, cuerpo (JSON,
texto o form) y los dos scripts. Los cambios se escriben sobre el mismo modelo que muestra el
árbol —igual que en el escritorio, así que se ven en el acto— y llegan al disco con el botón
**Guardar**; de ahí al equipo, con **Sincronizar**. Guardar es un botón y no un efecto de escribir
justamente porque la colección es compartida: un roce en el colectivo no tiene por qué terminar en
el repo de todos.

> Antes el editor era de sólo lectura a propósito. Se cambió cuando el móvil dejó de ser un spike
> de diagnóstico: con el servidor de sync andando hay respaldo e historial, que es lo que faltaba
> para que editar desde el teléfono no fuera un riesgo.

## Layout adaptativo: teléfono, tablet y fold

El layout lo decide **el ancho disponible**, no el tipo de aparato. Un fold desplegado, un teléfono
en horizontal y una ventana en multiventana son el mismo problema, y el ancho es lo único que lo
describe bien:

| Ancho | Qué se ve |
|---|---|
| < 600 dip | Una columna: la lista, y el detalle la reemplaza con botón de volver |
| ≥ 600 dip | Lista fija a la izquierda (300 dip) y detalle al lado, sin navegar |
| ≥ 900 dip | Igual, con la lista un poco más ancha (360 dip) |

600 es el corte con el que Android define "pantalla grande"; coincide con cualquier tablet y con un
fold desplegado.

Dos decisiones que hacen que el fold no moleste:

- **Las dos vistas viven siempre.** Cambiar de modo sólo toca el ancho de las columnas y la
  visibilidad, así que plegar y desplegar no reconstruye nada ni pierde lo que estabas escribiendo.
- **La actividad declara los cambios de configuración** (`ScreenLayout` y `SmallestScreenSize`
  además de los de siempre, en `MainActivity`). Sin eso Android recrea la actividad al desplegar
  —la pantalla no rota, cambia de tamaño— y se pierde la request abierta. El manifiesto además
  declara `resizeableActivity` explícito, para multiventana.

Y una que hace usable un workspace de verdad: **el buscador de la lista**. Con doscientas requests
importadas de un OpenAPI, bajar scrolleando no es una opción; mientras hay filtro, las carpetas
plegadas no esconden resultados.

## Qué se puede hacer

| | |
|---|---|
| **Colecciones** | crear, renombrar y eliminar colecciones, carpetas y subcarpetas; duplicar y eliminar requests. El menú «⋯» de cada nodo es el click derecho del escritorio traducido a algo que se pueda tocar |
| **Editor** | método, URL, query params, cabeceras, auth (heredada, Bearer, Basic, API key), cuerpo (JSON con formateo, texto o form) y los dos scripts |
| **Respuesta** | estado, tiempo y tamaño; cuerpo (JSON indentado), cabeceras y tests en solapas; botón de copiar |
| **Ambientes** | crear, renombrar, eliminar y editar variables, con el activo elegido desde la barra |
| **Importar** | pegando: la **URL** de un OpenAPI (se baja sola), el documento entero (JSON o YAML), o un cURL para crear una request |
| **Runner** | usuarios virtuales, iteraciones o duración, ramp-up y delay, con métricas en vivo |
| **Sync** | conectarse a un servidor, elegir workspace y sincronizar |

Todo se apoya en el Core: el importador, el parser de cURL y el motor del runner son los mismos
que usa el escritorio. La app de Android es pantalla, no una segunda implementación.

### Variables secretas

Una variable de ambiente se puede marcar como secreta y su valor va enmascarado, con un botón
para revelarlo. **No es cifrado**: en el disco del teléfono sigue en claro, igual que en el
escritorio. Lo que resuelve es mostrar la pantalla en una reunión o en el subte sin exponer un
token.

La marca se guarda como `secretKeys`, el mismo nombre que usa el documento de ambiente que viaja
al servidor de sync (`Services/Sync/EnvironmentDocument`). Es a propósito: hoy la app todavía no
sincroniza ambientes —falta la pieza del lado del cliente, ver [SYNC.md](SYNC.md#límites-conocidos)—
y cuando la tenga, lo que hay que cifrar ya está expresado y no hay que migrar nada.

### Runner: qué mide

Correr carga desde un teléfono **mide también al teléfono y a su red**. Sirve para ver cómo
responde un servicio desde afuera, no para sacar números de capacidad.

No trae el gráfico temporal ni la comparación de corridas: en una pantalla de teléfono el gráfico
es decorado y comparar dos corridas se hace sentado. Las corridas **se guardan**, así que la
comparación queda disponible desde el escritorio.

## Login: hace falta habilitar el esquema en el server

En el escritorio el redirect de OAuth vuelve por un puerto local; en el teléfono vuelve por un
esquema propio. Para que ande, el servidor de sync tiene que aceptarlo:

```
Auth__AllowedRedirectSchemes__0=easyrest
```

Sin eso el server rechaza el `redirect_uri` y el login falla antes de abrir el navegador. Se abre
el navegador del sistema y no una WebView adentro de la app, así el login usa las sesiones que la
persona ya tiene y la app nunca ve la contraseña.

## Administración: desde el escritorio

La pantalla de sync del teléfono hace lo mínimo: conectarse, elegir de qué workspace bajar y
cerrar sesión. Invitar gente, cambiar roles o crear workspaces se hace desde la app de escritorio
o la consola del server. Invitar a alguien desde el colectivo no es un caso real, y esa pantalla
en un teléfono sería peor que abrir la computadora.

## Bajar el APK

`EasyRest-android.apk` va **adjunto a cada Release**, junto a los binarios de escritorio. Se
instala en cualquier teléfono con "orígenes desconocidos" habilitado.

El job `android-spike` además lo arma en **cada corrida** y lo deja como artefacto (*Actions → la
corrida → Artifacts → `EasyRest-android-apk`*), que es la forma de probar algo antes de publicarlo.

La versión del APK sale de la misma fuente que la de los binarios de escritorio: el tag, o el
input del disparo manual. El `versionCode` que pide Android se deriva de ahí (0.1.13 → 113), que
es un entero que siempre crece.

### La firma, y por qué importa más ahora

Sin keystore propio, .NET Android firma con **una clave de depuración que se genera por máquina**,
y los runners del CI son efímeros: cada corrida firma con una clave distinta. Android rechaza
instalar encima de un APK con otra firma, así que actualizar obliga a desinstalar primero
(`adb uninstall com.rentlysoft.easyrest`) y se pierden los datos locales de la app.

Eso era tolerable cuando el APK era un artefacto de CI para probar. **Publicado en un release ya
no lo es**: la promesa de un release es que se instala encima del anterior.

El workflow ya está preparado y se prende solo cuando existen los secrets, igual que la firma de
Windows y macOS:

| Secret | Qué es |
|---|---|
| `ANDROID_KEYSTORE_B64` | el `.keystore` en base64 (`base64 -w0 easyrest.keystore`) |
| `ANDROID_KEY_ALIAS` | el alias de la clave dentro del keystore |
| `ANDROID_KEYSTORE_PASSWORD` | la contraseña del keystore |
| `ANDROID_KEY_PASSWORD` | la de la clave, si es distinta de la anterior |

Para generarlo, una vez:

```bash
keytool -genkeypair -v -keystore easyrest.keystore -alias easyrest \
  -keyalg RSA -keysize 2048 -validity 10000
base64 -w0 easyrest.keystore    # esto va en ANDROID_KEYSTORE_B64
```

**Guardá el keystore fuera del CI.** Si se pierde, no hay forma de firmar una actualización de la
app que ya instaló la gente: hay que cambiar el `applicationId` y volver a empezar.

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

### El tema tiene que ser AppCompat

`AvaloniaMainActivity` hereda de `AppCompatActivity`, y esa clase exige un tema descendiente de
`Theme.AppCompat`. Con un tema de la plataforma —`@android:style/Theme.Material.Light.NoActionBar`,
que era lo que tenía— la app se cierra al instante:

```
java.lang.IllegalStateException: You need to use a Theme.AppCompat theme (or descendant)
   at Avalonia.Android.AvaloniaActivity.set_Content(Object)
```

El tema propio está en `Resources/values/styles.xml`, hereda de `Theme.AppCompat.NoActionBar` y
pinta el fondo de la ventana del color de la app para que no haya un flash blanco antes del
primer frame.

Esto no lo agarra el CI: el APK se arma perfecto y `aapt2` no tiene forma de saber que la
actividad va a pedir otro tema. **Sólo se ve corriéndolo en un dispositivo**, que es exactamente
para lo que existe el spike.

### La red de seguridad: una pantalla de diagnóstico en Android puro

Cuando el spike se cae, la app **no** abre en Avalonia sino en una pantalla hecha con `TextView` y
`Button` de Android. No es pereza: la primera versión se cerraba al instante y una pantalla de
error escrita en Avalonia no servía para nada, porque el crash ocurría **antes** de que Avalonia
llegara a dibujar. Esta actividad no depende de nada del stack que se está probando, así que la
app siempre abre y siempre puede contar qué pasó en el intento anterior.

Si no hay nada que contar se saltea sola y va derecho al spike: la red de seguridad no tiene por
qué cobrarle un toque de más a cada arranque.

Muestra tres cosas:

- **La excepción** del intento anterior, si quedó registrada (`crash.txt`).
- **El rastro** de hasta dónde llegó (`trace.txt`): cada paso del arranque escribe una miga en
  disco apenas ocurre. Si el rastro se corta y no hay excepción, el proceso murió sin poder
  contarlo — que es la firma de un crash nativo o de una falla del runtime.
- **El entorno**: arquitectura, runtime, si genera código en tiempo de ejecución.

Hay un botón para compartir el informe entero, así no hace falta el cable. Si aun así se quiere
el log del sistema:

```bash
adb logcat -c            # limpiar, abrir la app, y después:
adb logcat -d | grep -iE "AndroidRuntime|mono-rt|EasyRest"
```

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

### Corre en un teléfono de verdad — verificado

Probado en un Galaxy con Android 16 (`Arm64`, .NET 8.0.30, Mono con JIT). El spike, en una sola
pantalla:

- **Los scripts corren.** Los dos `er.test` pasan, el `console.log` sale y el `er.setVar` vuelve
  al ambiente (`ultimoEstado = 200`). O sea que **Jint funciona en Android con el trimming del
  SDK activado**, no sólo en la sonda de NativeAOT.
- **El almacenamiento anda.** Escribe y lee en `/data/user/0/com.rentlysoft.easyrest/files/.config`,
  que es de donde va a leer el `Storage` del Core.
- **La request sale.** `200 OK · 1251 ms · 39 bytes` contra `api.github.com`, con el cuerpo
  renderizado.

El APK pesa **38 MB**.

Esto es lo que la sonda de AOT no podía contestar: la sonda prueba el Core sin JIT en Linux, pero
no dice nada del runtime de Android, del sistema de archivos del teléfono ni de la red del
dispositivo.

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

Lo técnico ya está contestado. Lo que queda es de uso, y sigue necesitando el teléfono en la mano:

- [x] ¿Corre el Core —scripts, storage, red— en un dispositivo real? **Sí.**
- [x] ¿Cuánto pesa el APK? **38 MB.**
- [ ] ¿Cómo se siente Avalonia en móvil? Scroll, tamaño de los toques.
- [ ] ¿El teclado en pantalla tapa el campo de la URL al escribir?
- [ ] ¿Cuánto tarda en abrir desde frío?
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
