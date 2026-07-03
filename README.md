# EasyRest

Cliente HTTP de escritorio para Windows, estilo Postman/Bruno. WPF + .NET 8.

## Ejecutar

```powershell
dotnet run --project D:\EasyRest\EasyRest.csproj
```

O compilar un ejecutable autocontenido:

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# queda en bin\Release\net8.0-windows\win-x64\publish\EasyRest.exe
```

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
- **Runner**: botón "▶ Runner", click derecho sobre una request o botón "⟳ Runner" del editor —
  ejecuta una request puntual o toda la colección en N iteraciones, con delay opcional, opción de
  frenar al primer error y grilla de resultados (status, tiempo, promedio).
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

## Estructura

- `Models/Models.cs` — colecciones, requests, ambientes, auth y body.
- `Services/Storage.cs` — persistencia en `%AppData%\EasyRest`.
- `Services/VariableResolver.cs` — reemplazo de `{{variables}}`.
- `Services/HttpExecutor.cs` — construcción y envío de las requests HTTP.
- `Services/OpenApiImporter.cs` — importación de OpenAPI (Microsoft.OpenApi.Readers).
- `MainWindow` / `EnvironmentsWindow` / `RunnerWindow` / `PromptWindow` — UI.
