# Spike de Android

Un head de Android mínimo sobre el mismo Core, para responder con datos si vale la pena llevar
EasyRest al teléfono. **No es un producto**: es una pantalla de diagnóstico que ejercita las dos
piezas que estaban en duda —el motor de scripts y el almacenamiento— además de mandar una request
de verdad.

## Correrlo

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
- El `AndroidManifest.xml` pide **permiso de INTERNET**: sin eso `HttpClient` falla en silencio,
  que en un cliente HTTP sería gracioso.
- **No está en `EasyRest.slnx`** a propósito: sin el workload instalado, tenerlo en la solución
  rompería el build para todos los demás. Se compila apuntándole al csproj, y en CI tiene su
  propio job.
