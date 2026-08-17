# Publicar el servidor de sync en la nube

No hay nada que armar: el server ya es una imagen de Docker
([`src/EasyRest.Sync.Server/Dockerfile`](../src/EasyRest.Sync.Server/Dockerfile)), corre en un
contenedor de 512 MB y guarda todo en un archivo SQLite dentro de `/data`. Desplegarlo es
construir esa imagen en cualquier plataforma que sepa leer un Dockerfile, montarle un disco y
darle cuatro variables.

Probarlo local antes de subirlo, exactamente lo que va a correr allá:

```bash
docker build -f src/EasyRest.Sync.Server/Dockerfile -t easyrest-sync .
docker run -p 8080:8080 -v easyrest-data:/data \
  -e EASYREST_MASTER_KEY="$(openssl rand -base64 32)" easyrest-sync
```

Si preferís una VM con systemd y sin Docker, ese camino está en [docs/INSTALL.md](INSTALL.md).

## Lo que la plataforma tiene que darte

| Requisito | Por qué |
|---|---|
| **Un volumen montado en `/data`** | ahí vive la base. Sin volumen, cada deploy arranca de cero |
| **Una sola instancia** | la secuencia de cambios se serializa en memoria por workspace ([límites conocidos](SYNC.md#límites-conocidos)). Dos réplicas rompen el orden |
| **HTTPS con dominio estable** | los IdP rechazan redirects `http://` que no sean localhost, y el dominio queda registrado allá: si cambia, el login deja de andar |
| **Variables de entorno** | la master key y las credenciales del IdP |

El contenedor expone `GET /health` para el health check y escucha en 8080, o en `PORT` si la
plataforma lo inyecta.

**Descartá de entrada** los planes gratuitos sin disco (Render free, Cloud Run, Container Apps
tal como vienen): ahí el filesystem es efímero y la base se pierde en cada arranque. Se pueden
usar apuntando a un Postgres administrado —el server lo soporta, ver `docker-compose.yml`— pero
eso es más piezas para mantener que un volumen de 1 GB.

## Qué elegir

Precios de referencia; verificá los actuales, cambian seguido.

| Opción | Costo | Cuándo conviene |
|---|---|---|
| **Railway** | ~USD 5/mes (Hobby, incluye USD 5 de uso) | el camino más corto: conectás el repo, agregás el volumen y listo |
| **Fly.io** | ~USD 2–4/mes | máquina chica con disco que se suspende sola cuando nadie la usa |
| **VPS con Docker** (Oracle Always Free, Hetzner) | USD 0 / ~EUR 4 | si ya administrás servidores. Oracle tiene una VM ARM gratis sin plazo |

Para un equipo chico, **Railway**: es el que menos partes tiene.

---

## Railway

El repo trae [`railway.json`](../railway.json), así que la plataforma ya sabe qué Dockerfile
construir, dónde está el health check y que va una sola réplica.

1. **New Project → Deploy from GitHub repo**, elegí este repo.
2. **Variables** del servicio:

   ```
   EASYREST_MASTER_KEY=<openssl rand -base64 32>
   Server__BehindProxy=true
   ConnectionStrings__Default=Data Source=/data/easyrest-sync.db
   Auth__ServerAdminEmails__0=vos@tuempresa.com
   Auth__AllowedEmailDomains__0=tuempresa.com
   RAILWAY_RUN_UID=0
   ```

   Esa última hace falta y no es opcional: **Railway monta el volumen como root** y la imagen
   corre con un usuario sin privilegios, así que sin ella el server arranca y muere con
   `SQLite Error 14: unable to open database file`. Es la respuesta documentada de Railway y
   tiene su costo —el contenedor pasa a correr como root—; en las otras plataformas no hace
   falta porque el montaje respeta al usuario de la imagen.

3. **Volume** montado en `/data`. Es el paso que más se olvida y el único irreversible.
4. **Settings → Networking → Generate Domain**, y con esa URL agregá
   `Auth__PublicUrl=https://<tu-app>.up.railway.app`.
5. Registrá el redirect en el IdP y cargá sus credenciales ([abajo](#el-idp)).

Un detalle del Dockerfile que conviene no deshacer: **no declara `VOLUME`**. Railway rechaza la
imagen entera si lo encuentra (*"docker VOLUME is not supported, use Railway Volumes"*), y el
build falla en dos segundos sin escribir un solo log, que es lo más caro de diagnosticar.

## Fly.io

El repo trae [`fly.toml`](../fly.toml) con el puerto, el health check, el volumen y una sola
máquina ya declarados.

```bash
fly launch --no-deploy               # toma el fly.toml del repo; elegí nombre y región
fly volumes create easyrest_data --size 1
fly secrets set EASYREST_MASTER_KEY="$(openssl rand -base64 32)" \
                Auth__PublicUrl="https://<tu-app>.fly.dev"
fly deploy
```

`auto_stop_machines` suspende la máquina cuando no hay tráfico y la despierta con la primera
request: es lo que hace que el server de un equipo chico cueste centavos. No la escales: una
máquina, `fly scale count 1`.

## Un VPS con Docker

Cualquier máquina con Docker alcanza, y es la opción más barata de todas (la VM ARM *Always Free*
de Oracle no vence). Es el `docker-compose.yml` del repo tal cual:

```bash
git clone https://github.com/guidozanon/easyrest && cd easyrest
cp .env.example .env      # completá EASYREST_MASTER_KEY y EASYREST_PUBLIC_URL
docker compose up -d
```

Falta el TLS, que en un VPS lo ponés vos. Con Caddy son dos líneas y se encarga del certificado:

```
sync.empresa.com {
    reverse_proxy 127.0.0.1:8080
}
```

Y en el compose, `Server__BehindProxy=true`.

---

## El IdP

Cualquiera sea la plataforma, el login necesita dos cosas que tienen que coincidir exactamente:

1. `Auth__PublicUrl` = la URL pública del server, con https y sin barra final.
2. En el IdP, una aplicación web con el redirect `<Auth__PublicUrl>/api/v1/auth/callback`.

Después van las credenciales, por env vars:

```
Auth__Providers__0__Id=google
Auth__Providers__0__DisplayName=Google
Auth__Providers__0__Kind=oidc
Auth__Providers__0__Authority=https://accounts.google.com
Auth__Providers__0__ClientId=…
Auth__Providers__0__ClientSecret=…
```

La consola en `/Admin` diagnostica esto —si el discovery del IdP responde, qué redirect registrar,
si la URL pública no coincide con el host por el que entrás— y es el primer lugar donde mirar
cuando el login falla.

### Microsoft Entra

Tres cosas que no son obvias y se pagan con un login que falla sin explicar:

- **La plataforma del redirect tiene que ser `Web`, no `Single-page application`.** Con SPA, Entra
  exige PKCE contra el IdP y rechaza el login con `AADSTS9002325`, aunque todo lo demás esté bien.
  El server es un cliente confidencial: guarda el client secret, así que va como Web.
- **La Authority lleva el tenant, no `common`.** El server valida que el emisor del `id_token` sea
  el del discovery, y `common` emite con un issuer que no coincide:
  `https://login.microsoftonline.com/<tenant-id>/v2.0`.
- **Permisos: `openid`, `profile` y `email` delegados, y nada más.** El server no llama a la Graph
  API. Si el tenant tiene apagado el consentimiento de usuario, hace falta el *Grant admin consent*
  una vez. Conviene además agregar `email` como optional claim del ID token, porque Entra no
  siempre lo manda y sin mail el server no puede identificar a la persona.

## Un server público no es un server interno

En una VM detrás de la VPN alcanza con instalarlo. Expuesto a internet, tres cosas antes de
repartir la URL:

- **Entrá vos primero.** El primer usuario que hace login se queda con la administración del
  server. Mejor todavía: dejá tu mail en `Auth__ServerAdminEmails__0` desde el primer deploy, que
  además es la forma de recuperar el acceso si te quedás afuera.
- **Acotá quién entra.** Con Google o GitHub como IdP, cualquiera con una cuenta puede pedir
  entrar: `Auth__AllowedEmailDomains__0=tuempresa.com`, y `Auth__AllowOpenRegistration=false`
  cuando ya está todo el equipo adentro.
- **`Server__BehindProxy=true`.** La plataforma termina el TLS y la request llega al contenedor
  como http; sin esa variable la cookie de la consola sale sin `Secure`.

## Backup

Dos cosas, y las dos hacen falta:

1. **La base**, `/data/easyrest-sync.db` — el volumen.
2. **`EASYREST_MASTER_KEY`** — guardala fuera de la plataforma el día que la generás.

Con la base pero sin la clave, los ambientes se restauran **sin sus secretos**: esa clave envuelve
las claves de datos de cada workspace y no se puede derivar de nuevo. Ojo con las plataformas
donde borrar y recrear un servicio es un botón: perder la variable es perder los secretos aunque
el volumen esté intacto.

## Variables que agregan estos entornos

Además de las de [docs/SYNC.md](SYNC.md#variables):

| Variable | Qué hace |
|---|---|
| `PORT` | si la plataforma lo inyecta, el server escucha ahí en vez de en 8080 |
| `Server__BehindProxy` | `true` cuando hay un proxy que termina TLS adelante: se respeta `X-Forwarded-Proto` |
