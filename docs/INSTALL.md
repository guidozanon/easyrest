# Instalar el servidor de sync en Linux

Dos caminos, ambos soportados: **el instalador** (binario autocontenido + systemd) o **Docker**.
Si ya vivís en Docker, andá a [docs/SYNC.md](SYNC.md#levantarlo). Si preferís una VM común y
corriente, esto es más directo.

No hace falta tener .NET instalado: el paquete trae el runtime adentro. **SQLite tampoco se
instala**: el proveedor de .NET trae la librería embebida y la base es un archivo.

## Instalación

```bash
curl -fsSLO https://github.com/guidozanon/easyrest/releases/latest/download/install.sh
sudo bash install.sh
```

El asistente pregunta la URL pública, el puerto, el proveedor de identidad y quiénes son
administradores del server. Después descarga el paquete, verifica su SHA-256, crea el usuario de
sistema, instala el servicio y lo arranca.

**La clave de cifrado la genera él**, no la pide: es la que protege los secretos de todos los
workspaces. Al terminar te dice dónde está — guardá una copia, porque un backup de la base sin
esa clave no sirve para recuperar secretos.

## Qué deja

```
/opt/easyrest-sync/              binarios (root, sólo lectura para el servicio)
/etc/easyrest-sync/server.env    configuración y clave de cifrado (0640 root:easyrest)
/var/lib/easyrest-sync/          la base SQLite (0700 easyrest)
/etc/systemd/system/easyrest-sync.service
```

```bash
systemctl status easyrest-sync
journalctl -u easyrest-sync -f
```

## TLS: hace falta

El server escucha en `127.0.0.1` a propósito: va detrás de un proxy que termine TLS. No es una
recomendación estética — **los IdP rechazan redirects `http://` que no sean localhost**, así que
sin TLS el login no funciona.

Con Caddy alcanza con dos líneas:

```
sync.empresa.com {
    reverse_proxy 127.0.0.1:8080
}
```

Con nginx:

```nginx
server {
    listen 443 ssl;
    server_name sync.empresa.com;
    # ssl_certificate ...;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

La URL pública que le diste al instalador tiene que ser la del proxy (`https://sync.empresa.com`),
no `http://127.0.0.1:8080`. Si no coinciden, la consola te lo avisa en el resumen.

## Registrar el redirect en el IdP

En tu proveedor, la aplicación web tiene que tener este redirect:

```
https://sync.empresa.com/api/v1/auth/callback
```

El instalador lo imprime al terminar, y la consola lo vuelve a mostrar en `/Admin` junto con el
diagnóstico del IdP (si el discovery responde, si falta el client secret, si la URL pública no
coincide con el host por el que estás entrando).

## Instalación desatendida

Todo flag tiene su variable de entorno equivalente, para Ansible o cloud-init:

```bash
sudo bash install.sh --unattended \
  --url https://sync.empresa.com \
  --provider oidc --provider-id google --provider-name Google \
  --authority https://accounts.google.com \
  --client-id "$CLIENT_ID" --client-secret "$CLIENT_SECRET" \
  --admin-email jefe@empresa.com \
  --email-domain empresa.com
```

En modo desatendido falla si falta algo obligatorio en vez de quedarse esperando una respuesta
que nunca llega.

Para instalar sin salir a internet, bajá el `.tar.gz` aparte y pasalo con `--tarball`.

## Actualizar

El mismo script:

```bash
sudo bash install.sh
```

Detecta la instalación existente, **conserva la configuración y la clave de cifrado**, reemplaza
los binarios y reinicia. Las migraciones de la base corren solas al arrancar. Hay tests que fallan
si alguna vez esto deja de ser cierto — regenerar la clave en una actualización dejaría todos los
secretos ilegibles.

## Desinstalar

```bash
sudo bash install.sh --uninstall           # conserva datos y configuración
sudo bash install.sh --uninstall --purge   # borra todo, clave incluida
```

## Detalles del servicio

El unit corre como el usuario `easyrest`, sin privilegios, con `ProtectSystem=strict` y permiso de
escritura únicamente sobre `/var/lib/easyrest-sync`.

Una advertencia si alguna vez lo endurecés más: **`MemoryDenyWriteExecute` tiene que quedar en
`no`**. El JIT de .NET necesita memoria ejecutable y con esa opción en `yes` el servicio no
arranca, con un error que no dice nada útil. Está comentado dentro del propio unit para que nadie
lo cambie sin querer.

## Backup

Dos cosas, y las dos hacen falta:

1. `/var/lib/easyrest-sync/easyrest-sync.db` — los datos.
2. `/etc/easyrest-sync/server.env` — **la clave de cifrado**.

Con la base pero sin la clave, los ambientes se restauran sin sus secretos. Con SQLite lo más
seguro es copiar en caliente con `sqlite3 easyrest-sync.db ".backup copia.db"`, o parar el
servicio un segundo.
