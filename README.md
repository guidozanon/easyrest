# EasyRest

Cliente HTTP de escritorio estilo Postman/Bruno. .NET 8.

## Estructura

- `src/EasyRest.Core` — modelos y servicios (storage/workspace, HTTP, OpenAPI, cURL, scripts con Jint,
  git, sync, logs, runner). **Sin dependencias de UI**: es la base compartida.
- `src/EasyRest.Avalonia` — la app multiplataforma (Windows/macOS/Linux) sobre el Core.
- `src/EasyRest.Sync.Server` — servidor de sync self-hosted, opcional. Ver
  [docs/SYNC.md](docs/SYNC.md).

## Ejecutar

```powershell
dotnet run --project src/EasyRest.Avalonia
```

Carpeta autocontenida para Windows:

```powershell
dotnet publish src/EasyRest.Avalonia -c Release -r win-x64 --self-contained
```

Para macOS (desde cualquier plataforma):

```bash
dotnet publish src/EasyRest.Avalonia -c Release -r osx-arm64 --self-contained
```

## Descargas

Un GitHub Action (`.github/workflows/build.yml`) compila binarios autocontenidos para
Windows (x64) y macOS (arm64/x64):

- **Release**: al pushear un tag `vX.Y.Z` se publica un Release con los binarios de cada plataforma.
- **Manual**: se puede correr el workflow a mano (*Actions → build → Run workflow*); los binarios
  quedan como artefactos de esa corrida.

Qué bajar según la plataforma:

| Archivo | Plataforma |
|---|---|
| `EasyRest-Setup-<versión>.exe` | Windows. Installer: instala en `%LocalAppData%\Programs\EasyRest`, sin permisos de admin |
| `EasyRest-win-x64-portable.zip` | Windows sin instalar (la misma carpeta, descomprimida donde quieras) |
| `EasyRest-macos-arm64.zip` | Mac con Apple Silicon (M1/M2/M3…) |
| `EasyRest-macos-x64.zip` | Mac con Intel |
| `EasyRest-linux-x64.tar.gz` | Linux x86_64 |
| `EasyRest-linux-arm64.tar.gz` | Linux arm64 |

### Firma

Los binarios se firman en el CI: `EasyRest.app` con Developer ID de Apple y notarización, y el
`.exe` y el installer de Windows con el certificado de [SignPath
Foundation](https://signpath.org) (gratis para proyectos open source). El detalle de cómo está
armado —y qué secrets necesita— está en [docs/FIRMA.md](docs/FIRMA.md).

En Windows puede seguir apareciendo el cartel de SmartScreen hasta que el certificado acumule
reputación: *Más información → Ejecutar de todas formas*.

### Auto update

La app se actualiza sola contra los Releases de GitHub. Al iniciar hace un chequeo silencioso
(como máximo una vez cada 20 h) y, si hay una versión nueva, aparece **⬆ vX.Y.Z disponible** en la
barra de estado. Desde ahí (o desde el menú ⋯ del sidebar → *Buscar actualizaciones…*, o *Acerca de
EasyRest*) se abre el panel de actualizaciones con las notas de la release y tres opciones:
descargar e instalar, omitir esa versión o cerrar. También se puede apagar el chequeo automático
con el check *Buscar actualizaciones al iniciar*.

Al instalar, EasyRest baja el binario de la plataforma, guarda todo lo pendiente y se cierra; un
script externo espera a que el proceso termine, reemplaza la instalación (la carpeta en Windows y
Linux, el bundle `EasyRest.app` en macOS) y vuelve a abrir la app en la versión nueva. Si el swap
falla, deja la versión anterior en su lugar. Si la carpeta de instalación no tiene permisos de
escritura, avisa antes de cerrar nada. Corriendo desde el código (`dotnet run`) el panel sólo
ofrece abrir la release en GitHub, para no pisar `bin/`.

> **Si venís de la v0.1.10 o anterior en Windows** (la que era un único `EasyRest.exe`): esa
> instalación no se actualiza sola a este formato. Bajá una vez el `EasyRest-Setup-<versión>.exe`
> y desde ahí vuelve a actualizarse automáticamente. El motivo está en
> [docs/FIRMA.md](docs/FIRMA.md#migración-de-los-que-ya-tienen-la-versión-single-file).

### macOS

El zip de Mac trae `EasyRest.app`, firmado con Developer ID de Apple y notarizado: se descomprime,
se mueve a `Aplicaciones` y abre normal, sin carteles de Gatekeeper.

Elegí el zip según tu Mac: `macos-arm64` (Apple Silicon M1/M2/M3…) o `macos-x64` (Intel).

<details>
<summary>Releases hasta la v0.1.10 (sin firmar)</summary>

Esos zips llevaban firma ad-hoc y Gatekeeper los bloquea la primera vez. En macOS 14 y anteriores
se abren con **click derecho → Abrir → Abrir**; en macOS 15 (Sequoia) y posteriores ese atajo ya no
existe y hay que ir a *Ajustes del Sistema → Privacidad y seguridad*, y ahí tocar **Abrir de todos
modos** en el aviso sobre EasyRest.

Si sale *"está dañado y no se puede abrir"* (por el atributo de cuarentena al bajarlo del
navegador), quitá la cuarentena en Terminal y abrilo:

```bash
xattr -dr com.apple.quarantine /Applications/EasyRest.app
open /Applications/EasyRest.app
```

</details>

## Funcionalidades

- **Colecciones con carpetas anidadas**: árbol a la izquierda, menú contextual (click derecho) para
  crear requests, carpetas y subcarpetas, renombrar, duplicar y eliminar. Cada request permite editar
  método, URL, query params (solapa Params sincronizada con la URL en ambas direcciones; los
  desactivados no se envían), headers, autenticación (Bearer, Basic, API Key en header o query) y
  body (JSON, texto o form-urlencoded).
- **Pestañas**: doble click sobre una request la abre en una pestaña; cada pestaña conserva su
  última respuesta. El visor de respuesta formatea JSON/XML automáticamente según el Content-Type,
  con selector manual (Auto/JSON/XML/Texto).
- **Configuración de colección**: doble click sobre una colección abre su pestaña de configuración:
  nombre, headers heredados (se agregan a todas las requests; si la request define la misma clave,
  gana la request) y autenticación heredada (las requests con auth None usan la de la colección).
- **Descripciones**: si el OpenAPI trae `description` en las operaciones, aparece un ícono ⓘ al lado
  de la request en el árbol con la descripción en el tooltip.
- **Ambientes con variables**: botón "Ambientes" para definirlas; se usan en cualquier campo con la
  sintaxis `{{nombre}}` (URL, headers, auth, body). El ambiente activo se elige en la barra superior.
  Desde la misma ventana se pueden **compartir**: *Compartir…* copia el ambiente como JSON al
  portapapeles (con valores, o solo claves para no filtrar tokens) o lo guarda en un archivo;
  *Importar…* lo trae desde el portapapeles o un archivo (acepta el formato propio y el export de
  environment de Postman). Si ya existe uno con el mismo nombre, ofrece reemplazar sus variables o
  crear una copia.
- **Runner (simulación de carga)**: el tab Runner es solo configuración — colección/request, ambiente,
  **usuarios virtuales** que corren en simultáneo, **ramp-up** (arranque escalonado), modo
  **Iteraciones** o **Duración (s)**, delay y frenar-en-error. Al tocar **Correr** se abre una pestaña
  de corrida con progreso, métricas en vivo (avg/p50/p95/p99/min/max, exitosas/fallidas, **req/s pico**,
  **tasa de error**), gráfico temporal de req/s + avg y grilla de resultados. Las corridas se pueden
  **guardar** y el tab **Comparar corridas** las pone lado a lado (tabla + gráfico req/s superpuesto).
  Las configuraciones se guardan como **presets** para reutilizarlas.
- **Importar OpenAPI**: botón "Importar OpenAPI" — acepta JSON o YAML (OpenAPI 2/3), genera una
  request por operación agrupando en carpetas anidadas por los segmentos del path
  (`/odata/bookings/{id}` → `odata` > `bookings`), con parámetros de path como variables
  (`{param}` → `{{param}}`), query params en la solapa Params (los opcionales quedan desactivados)
  y body JSON de ejemplo a partir del schema. Si el documento define `servers`, crea automáticamente
  un ambiente con la variable `baseUrl`. Hay un spec de prueba en `samples/petstore.json`.
- **Scripts (JavaScript via Jint)**: cada request tiene solapa Scripts con *Pre-request* (corre antes
  de enviar; puede tocar `er.request.url/method/body/setHeader` y variables con `er.getVar/setVar`) y
  *Post-response* (asserts con `er.test(nombre, condición)`, `er.response.status/body/getHeader`,
  extracción de variables — p. ej. guardar un token con `er.setVar("token", JSON.parse(er.response.body).access_token)`).
  `console.log` se captura y se ve junto a los resultados en la solapa Tests de la respuesta; el Runner
  también corre los scripts y cuenta los tests fallidos como fallas.
- **Workspace + Git**: desde el menú ⋯ del sidebar ("Workspace y Git…") podés apuntar las colecciones
  a una carpeta propia, clonar un repo existente, inicializar git, conectar un remote y sincronizar
  (add → commit → pull --rebase → push; la auth la resuelve tu credential manager). Si el pull trae
  conflictos, un popup pregunta si querés quedarte con la versión del remoto o pisar con la tuya.
  Los ambientes y settings quedan siempre en AppData: los tokens no van al repo. La barra de estado
  muestra `⎇ rama · N cambios`.
- **Servidor de sync (opcional)**: como alternativa a git, cada organización puede correr su propio
  servidor y sincronizar colecciones y ambientes **con secretos**, invitando gente a workspaces con
  roles y permisos. Login contra el IdP de la empresa (OIDC — Google, Entra, Okta, Keycloak… — o
  GitHub), y una consola web en `/Admin` para administrar usuarios y workspaces. Ver
  [docs/SYNC.md](docs/SYNC.md).
- **Auto update**: chequeo silencioso al iniciar contra los Releases de GitHub y panel de
  actualizaciones (barra de estado, menú ⋯ del sidebar o *Acerca de EasyRest*) que baja el binario de
  la plataforma, reemplaza la instalación y reinicia la app. Ver [Auto update](#auto-update).
- **Persistencia local**: por defecto todo se guarda como JSON en `%AppData%\EasyRest`. Cada
  colección es una carpeta (`collections/{Nombre}/`) con un `collection.json` de metadata, un
  archivo `{Request}.req.json` por request y un subdirectorio (con `folder.json`) por carpeta —
  diffs por request y menos conflictos de git. El formato viejo (un `.json` por colección) se
  migra automáticamente al abrir. Se guarda al enviar una request, con el botón Guardar, con
  **Ctrl+S / Cmd+S** (guarda todo lo pendiente: pestañas con cambios, colecciones, ambientes y
  settings) y al cerrar la app.

## Core (base compartida)

- `Models/Models.cs` — colecciones, requests, ambientes, auth y body.
- `Services/Storage.cs` — persistencia en `%AppData%\EasyRest` y workspaces.
- `Services/VariableResolver.cs` — reemplazo de `{{variables}}`.
- `Services/HttpExecutor.cs` — construcción y envío de las requests HTTP.
- `Services/OpenApiImporter.cs` — importación de OpenAPI (Microsoft.OpenApi.Readers).
- `Services/UpdateService.cs` — auto update: consulta de la última release, descarga del zip de la
  plataforma y script de reemplazo + reinicio.
- `RunnerTab` / `RunTab` / `RunComparisonTab` — configuración, ejecución y comparación de corridas.

La UI vive en `src/EasyRest.Avalonia` (`MainWindow` + `Views/`).
