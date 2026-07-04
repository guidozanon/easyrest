# EasyRest

Cliente HTTP de escritorio estilo Postman/Bruno. .NET 8.

## Estructura

- `src/EasyRest.Core` — modelos y servicios (storage/workspace, HTTP, OpenAPI, cURL, scripts con Jint,
  git, logs, runner). **Sin dependencias de UI**: es la base compartida.
- `src/EasyRest.Avalonia` — la app multiplataforma (Windows/macOS/Linux) sobre el Core.

## Ejecutar

```powershell
dotnet run --project src/EasyRest.Avalonia
```

Ejecutable autocontenido para Windows:

```powershell
dotnet publish src/EasyRest.Avalonia -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Para macOS (desde cualquier plataforma):

```bash
dotnet publish src/EasyRest.Avalonia -c Release -r osx-arm64 --self-contained
```

## Descargas

Un GitHub Action (`.github/workflows/build.yml`) compila binarios autocontenidos para
Windows (x64) y macOS (arm64/x64):

- **Release**: al pushear un tag `vX.Y.Z` se publica un Release con los `.zip` de cada plataforma.
- **Manual**: se puede correr el workflow a mano (*Actions → build → Run workflow*); los binarios
  quedan como artefactos de esa corrida.

### macOS

El zip de Mac trae `EasyRest.app`. Como **no está firmado** (haría falta cuenta de Apple Developer),
Gatekeeper lo bloquea la primera vez. Para abrirlo:

1. Descomprimí y movelo a `Aplicaciones`.
2. **Click derecho → Abrir → Abrir** (así se abre "de un desarrollador no identificado").

Si sale *"está dañado y no se puede abrir"* (por el atributo de cuarentena al bajarlo del navegador),
quitá la cuarentena en Terminal y abrilo:

```bash
xattr -dr com.apple.quarantine /Applications/EasyRest.app
open /Applications/EasyRest.app
```

Elegí el zip según tu Mac: `macos-arm64` (Apple Silicon M1/M2/M3…) o `macos-x64` (Intel).

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
  a una carpeta propia (se guardan como `collections/{Nombre legible}.json`, difeables en un repo),
  clonar un repo existente, inicializar git, conectar un remote y sincronizar (add → commit →
  pull --rebase → push; la auth la resuelve tu credential manager). Los ambientes y settings quedan
  siempre en AppData: los tokens no van al repo. La barra de estado muestra `⎇ rama · N cambios`.
- **Persistencia local**: por defecto todo se guarda como JSON en `%AppData%\EasyRest`
  (`collections\*.json`, `environments.json`, `settings.json`). Se guarda al enviar una request,
  con el botón Guardar y al cerrar la app.

## Core (base compartida)

- `Models/Models.cs` — colecciones, requests, ambientes, auth y body.
- `Services/Storage.cs` — persistencia en `%AppData%\EasyRest` y workspaces.
- `Services/VariableResolver.cs` — reemplazo de `{{variables}}`.
- `Services/HttpExecutor.cs` — construcción y envío de las requests HTTP.
- `Services/OpenApiImporter.cs` — importación de OpenAPI (Microsoft.OpenApi.Readers).
- `RunnerTab` / `RunTab` / `RunComparisonTab` — configuración, ejecución y comparación de corridas.

La UI vive en `src/EasyRest.Avalonia` (`MainWindow` + `Views/`).
