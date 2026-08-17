# Servidor de sync

Sincroniza colecciones y ambientes entre dispositivos y personas, con invitaciones a workspaces
y ambientes compartidos **con secretos**. Está pensado para que **cada organización corra el
suyo**: los datos nunca salen de su infraestructura, y el login se enchufa al IdP que ya usan.

Convive con el sync por git, no lo reemplaza. Ambos implementan `IWorkspaceSync` en el Core:
`GitWorkspaceSync` (el repo de siempre) y `RemoteWorkspaceSync` (este servicio, y la única que
funciona en móvil, donde no existe el CLI de git).

| | Git | Servidor de sync |
|---|---|---|
| Infraestructura | ninguna | un contenedor por organización |
| Secretos compartidos | no (los tokens quedan fuera del repo) | sí, cifrados |
| Invitar gente | por el hosting del repo | invitaciones con rol y permiso |
| Móvil | no | sí |

---

## Levantarlo

Con Docker:

```bash
cp .env.example .env
openssl rand -base64 32          # va en EASYREST_MASTER_KEY
docker compose up -d
```

Con eso queda andando en `:8080` sobre SQLite. Para Postgres, descomentá el servicio `db` y las
dos variables de conexión en `docker-compose.yml`.

Sin Docker, en una VM común, hay un instalador que deja el binario, la base y un servicio de
systemd, y pregunta lo que haga falta:

```bash
curl -fsSLO https://github.com/guidozanon/easyrest/releases/latest/download/install.sh
sudo bash install.sh
```

El detalle —TLS, modo desatendido, actualización, backup— está en
[docs/INSTALL.md](INSTALL.md).

Para subirlo a una plataforma administrada en vez de una máquina propia (Railway, Fly, o un VPS
con Docker), con lo que cuesta cada una y lo que hay que configurar, está
[docs/DEPLOY.md](DEPLOY.md).

### Configurar el IdP

El server no conoce proveedores: implementa **OIDC genérico** (Google, Microsoft Entra, Okta,
Auth0, Keycloak, Authentik…) y, aparte, **GitHub**, que es OAuth2 sin OIDC. Configurar uno nuevo
es cargar variables, no escribir código.

En el IdP registrá una aplicación web con este redirect:

```
<EASYREST_PUBLIC_URL>/api/v1/auth/callback
```

y pasá `Authority`, `ClientId` y `ClientSecret` por env vars (ver `.env.example`). Se pueden
configurar varios providers a la vez: la app pregunta por `/api/v1/meta` cuáles hay y dibuja los
botones que correspondan.

Si el IdP es público (Google, GitHub) conviene acotar quién entra:

```
Auth__AllowedEmailDomains__0=tuempresa.com
```

### Variables

| Variable | Qué hace |
|---|---|
| `EASYREST_MASTER_KEY` | **obligatoria**. 32 bytes en base64. Cifra las claves de datos de cada workspace |
| `Auth__PublicUrl` | URL pública del server, la que ve el IdP |
| `Auth__AllowedRedirectSchemes__N` | esquemas propios permitidos para el login móvil (`easyrest`) |
| `Auth__AllowedEmailDomains__N` | dominios de mail habilitados (vacío = todos) |
| `Auth__AllowOpenRegistration` | si es `false`, no entra nadie nuevo después del primero |
| `Auth__AccessTokenMinutes` / `Auth__RefreshTokenDays` | duración de la sesión |
| `Auth__ServerAdminEmails__N` | mails que reciben admin del server al entrar (vacío = el primero) |
| `Admin__Enabled` | `false` apaga la consola web y deja sólo la API |
| `Database__Provider` | `sqlite` (default) o `postgres` |
| `ConnectionStrings__Default` | cadena de conexión |

---

## Cómo está armado

### Login: la app nunca habla con el IdP

La app se autentica **contra el server de la organización**, y el server es el que habla con
Google o con quien sea. Así el cliente queda agnóstico de proveedores y no lleva client IDs de
nadie adentro.

```
app → GET /api/v1/meta                     ¿qué providers tenés?
app → abre el navegador del sistema en /api/v1/auth/start?provider=…&code_challenge=…
                                            (nunca un webview embebido)
        server → IdP → server /auth/callback
        server → redirige a la app con un authorization code propio
app → POST /api/v1/auth/token  {code, code_verifier}   → access + refresh
```

Es **Authorization Code + PKCE con cliente público**: no hay client secret en la app, porque en
una app de escritorio o móvil un secreto no es secreto. Los redirects aceptados son sólo loopback
(`http://127.0.0.1:*`) y los esquemas propios registrados (`easyrest://`) — cualquier otro se
rechaza, porque un redirect abierto acá es entregarle el código a cualquiera.

Los tokens son **opacos y revocables** (en la base sólo vive su SHA-256), no JWT: en un server
self-hosted la revocación inmediata vale más que ahorrarse una consulta. El refresh **rota**: al
usarlo, el anterior muere.

Para CI y uso headless hay **tokens de servicio**, atados a un workspace y con su propio rol.
Se distinguen a simple vista por el prefijo `ert_`.

### La consola de administración

En `/Admin` hay una consola web para el operador del server: resumen, usuarios y workspaces. Pide
`IsServerAdmin` y entra con el mismo login que la app.

Está hecha con Razor Pages dentro del mismo proyecto y el mismo contenedor: son unas pocas
pantallas de ABM y no justifican una cadena de build de node, un bundle ni CORS. El CSS va inline,
así que no hay ningún recurso externo que cargar.

Tres decisiones que vale la pena conocer:

- **La configuración de auth no se edita desde acá**, se muestra. Los providers cambian una vez
  por instalación, traen client secrets, y configurar el login desde una pantalla que exige estar
  logueado es un problema de huevo y gallina. Lo que sí hace la consola es **diagnosticar**: si el
  discovery del IdP responde, qué redirect hay que registrar allá, y si `Auth:PublicUrl` no coincide
  con el host por el que estás entrando o no es https — que es donde se pierden las tardes al
  instalar.
- **Nunca muestra el valor de un secreto.** Administra permisos, no contenidos. Un admin que
  necesite ver un token puede darse permiso y leerlo desde la app, y eso queda registrado; una
  consola que descifra inline convertiría la exfiltración silenciosa en dos clics.
- **Se puede apagar entera** con `Admin__Enabled=false`, para quien quiera exponer sólo la API.

La sesión es el mismo access token opaco de la API, guardado en una cookie `HttpOnly` (`Secure`
cuando la request es https, `SameSite=Lax`). No hay un segundo sistema de sesiones: revocar el
token o desactivar a la persona corta también la consola. Los formularios llevan antiforgery.

Repartir permisos sobre ambientes **no** está acá a propósito: eso se hace desde la app, que es
donde estás mirando el ambiente cuando decidís quién ve sus tokens.

### Documentos y revisiones

Cada archivo del workspace es un documento con una **revisión opaca** y un **número de secuencia**
dentro del workspace. El cliente guarda la última secuencia que vio y pide sólo lo posterior:

```
GET /api/v1/workspaces/{id}/changes?since=<cursor>&limit=200
```

Eso es lo que hace viable el móvil: nunca se baja el workspace entero. Los borrados viajan como
**tombstones** (el documento queda con `deleted: true`), porque si no un borrado nunca llegaría al
otro dispositivo.

Para escribir se usa `If-Match`, con la semántica de HTTP:

| `If-Match` | Significa |
|---|---|
| ausente | crear. `409` si ya hay un documento vivo en ese path |
| `<rev>` | actualizar esperando esa revisión. `409` si cambió |
| `*` | pisar sin preguntar |

El `409` devuelve **la versión del server** en el cuerpo, para poder resolver sin perder nada.

La granularidad es un archivo por documento —un `.req.json` por request— que es la misma que ya
usaba el storage en disco para que los diffs de git fueran chicos. Acá paga de nuevo: dos personas
editando requests distintas de la misma colección no chocan nunca.

### Secretos

Los valores marcados como secretos **no viajan dentro del documento**. Van por un endpoint aparte
y cifrados:

- **Envelope encryption**: cada workspace tiene su clave de datos (AES-256-GCM), guardada envuelta
  con la master key del server. Rotar la master key es re-envolver las claves de datos, sin tocar
  un solo secreto.
- El id del documento y el nombre de la variable van como *associated data*: un ciphertext no se
  puede mover de un ambiente a otro ni de una variable a otra sin que falle el descifrado.
- El server **valida** que un ambiente no traiga valores secretos dentro del `content`. Sin esa
  validación el permiso sería decorativo: alcanzaría con mandar el token en el documento para que
  lo viera todo el mundo.

El permiso es `canReadSecrets` por miembro, con **excepción por ambiente** (`secret-access`). A
quien no lo tiene, el server le manda las claves con el valor vacío — que es exactamente el
"compartir sólo las claves" que la app ya tenía en `EnvironmentShare.ToJson(env, includeValues:
false)`. Y si esa persona edita el ambiente, sus secretos vacíos **se ignoran**: puede tocar el
resto sin pisarle los tokens a nadie.

### Roles

| Rol | Puede |
|---|---|
| `Member` | leer y escribir documentos |
| `Admin` | además: invitar, cambiar permisos, crear tokens de servicio |
| `Owner` | además: borrar el workspace y transferirlo. No se lo puede sacar ni degradar |

Cuando alguien no tiene acceso a un workspace, la respuesta es `404` y no `403`: un `403` ya
confirmaría que ese workspace existe.

### Administración del server

Aparte de los roles de workspace existe `IsServerAdmin`, que es otra cosa: administra el server,
no el contenido. Ve todos los usuarios y workspaces (`/api/v1/admin/…`), puede desactivar gente y
puede transferir el ownership de cualquier workspace.

Lo recibe **la primera persona que entra** — un server recién instalado no tiene a quién pedirle
permiso — y también cualquiera cuyo mail esté en `Auth__ServerAdminEmails__N`. Esa allowlist se
aplica en cada login, así que es la forma de recuperar el acceso si el admin original ya no está:
se agrega el mail, se reinicia y listo.

**Desactivar** a alguien (`PATCH /api/v1/admin/users/{id}` con `disabled: true`) le revoca las
sesiones en el acto y le impide volver a entrar. Se prefiere sobre borrar: borrarlo perdería la
autoría de todo lo que haya escrito. Dos protecciones contra quedarse afuera del propio server:
nadie se puede desactivar a sí mismo, y no se puede sacar al último administrador.

**Transferir el ownership** (`POST /api/v1/workspaces/{id}/transfer-ownership`) lo puede hacer el
owner o un administrador del server; el dueño anterior baja a `Admin`, no se lo saca. Sin esto un
workspace quedaría sin quien lo administre en cuanto esa persona se fuera, porque el rol de owner
no se puede sacar ni degradar.

---

## El cliente

`RemoteWorkspaceSync` (en `EasyRest.Core/Services/Sync/`) es **offline-first**: el disco manda.
Cada sincronización baja el delta desde el cursor, sube lo que cambió localmente y guarda el
estado en un `.sync-state.json` con el hash de cada archivo — el hash es lo que distingue "no lo
toqué" de "lo edité".

Ante ediciones cruzadas, **gana lo local** y la versión del server queda al lado como
`<archivo>.remoto-<rev>.json`. Nunca se pierde una edición sin dejar rastro. La UI puede pedir otra
resolución (`KeepRemote` / `KeepLocal`), igual que ya hacía con los conflictos de git.

Sólo se sincronizan las carpetas `collections/` y `environments/`. Es deliberado: la raíz del
workspace personal es AppData, donde viven `settings.json` y `environments.json` con los tokens
locales, y esos **no se suben nunca**.

Los ambientes viajan partidos: en disco tienen los valores completos porque la app los necesita, y
al subir se separan según la lista `secretKeys` del propio ambiente:

```json
{
  "id": "e1", "name": "Producción",
  "secretKeys": ["token"],
  "variables": [
    { "key": "baseUrl", "value": "https://api.example.com" },
    { "key": "token",   "value": "" }
  ]
}
```

---

## Límites conocidos

Vale tenerlos a la vista antes de ponerlo en producción:

- **Una sola instancia.** La asignación de secuencia se serializa en memoria por workspace. Para
  correr varias réplicas hay que mover ese lock a la base.
- **Los secretos los puede leer el operador del server.** Es el mismo límite de confianza que el
  vault interno de la empresa, y es lo que permite que invitar a alguien funcione al instante. La
  alternativa es cifrado de punta a punta, con un costo concreto: una invitación no se completa
  hasta que un miembro que ya tiene la clave esté online para envolverla para el nuevo.
- **En el dispositivo los secretos quedan en JSON plano**, como hasta ahora. El siguiente paso de
  endurecimiento es guardarlos en el keychain del sistema (DPAPI, Keychain, Keystore).
- **Falta la UI en la app.** El server, la consola de administración y el cliente de sync están
  completos y testeados; lo que falta es la pantalla de login, el selector de workspace y la
  administración de miembros dentro de EasyRest.
- **La sesión de la consola dura lo que el access token** (una hora por defecto). Cuando vence hay
  que volver a entrar, que con la sesión del IdP viva es un ida y vuelta silencioso.

## Cambiar el esquema

El esquema se aplica con **migraciones de EF Core** al arrancar (`Database.Migrate()`), que es lo
que hace que reinstalar el server sea actualizarlo. Como se soportan dos proveedores y cada uno
necesita su propio SQL, hay dos assemblies de migraciones y **un cambio de modelo hay que
agregarlo en los dos**:

```bash
Database__Provider=sqlite dotnet ef migrations add <Nombre> \
  --project src/EasyRest.Sync.Server.Migrations.Sqlite \
  --startup-project src/EasyRest.Sync.Server

Database__Provider=postgres dotnet ef migrations add <Nombre> \
  --project src/EasyRest.Sync.Server.Migrations.Postgres \
  --startup-project src/EasyRest.Sync.Server
```

El modelo vive en `src/EasyRest.Sync.Data` justamente por esto: si el `DbContext` estuviera dentro
del server, la referencia entre server y migraciones sería circular.

Hay tests que fallan si alguien vuelve a `EnsureCreated` — verifican que la base tenga migraciones
aplicadas y ninguna pendiente. No son ceremonia: sin ese camino, cualquier instalación existente
queda sin forma de actualizarse.

## Versionado

La API es `/api/v1` y `/api/v1/meta` declara `apiVersions` y `capabilities`. Con servers
self-hosteados el skew es inevitable: los clientes tienen que mirar `capabilities` antes de usar
algo nuevo, y degradar con un mensaje claro en vez de tirar un 404 incomprensible.
