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

```bash
cp .env.example .env
openssl rand -base64 32          # va en EASYREST_MASTER_KEY
docker compose up -d
```

Con eso queda andando en `:8080` sobre SQLite. Para Postgres, descomentá el servicio `db` y las
dos variables de conexión en `docker-compose.yml`.

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
| `Owner` | además: borrar el workspace. No se lo puede sacar ni degradar |

Cuando alguien no tiene acceso a un workspace, la respuesta es `404` y no `403`: un `403` ya
confirmaría que ese workspace existe.

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
- **El esquema se crea con `EnsureCreated`.** Alcanza para empezar, pero antes del primer cambio de
  esquema hay que pasar a migraciones de EF Core, o los que ya self-hostearon no van a poder
  actualizar.
- **Los secretos los puede leer el operador del server.** Es el mismo límite de confianza que el
  vault interno de la empresa, y es lo que permite que invitar a alguien funcione al instante. La
  alternativa es cifrado de punta a punta, con un costo concreto: una invitación no se completa
  hasta que un miembro que ya tiene la clave esté online para envolverla para el nuevo.
- **En el dispositivo los secretos quedan en JSON plano**, como hasta ahora. El siguiente paso de
  endurecimiento es guardarlos en el keychain del sistema (DPAPI, Keychain, Keystore).
- **No hay UI todavía.** El server y el cliente están completos y testeados; falta la pantalla de
  login, el selector de workspace y la administración de miembros en la app.

## Versionado

La API es `/api/v1` y `/api/v1/meta` declara `apiVersions` y `capabilities`. Con servers
self-hosteados el skew es inevitable: los clientes tienen que mirar `capabilities` antes de usar
algo nuevo, y degradar con un mensaje claro en vez de tirar un 404 incomprensible.
