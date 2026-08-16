#!/usr/bin/env bash
#
# Instalador del servidor de sync de EasyRest para Linux, sin Docker.
#
#   curl -fsSLO https://github.com/guidozanon/easyrest/releases/latest/download/install.sh
#   sudo bash install.sh
#
# Instala un binario autocontenido (no hace falta tener .NET), crea el usuario de sistema, la
# base SQLite y un servicio de systemd, y pregunta lo que haga falta. Volver a correrlo sobre
# una instalación existente la actualiza sin tocar la configuración ni la clave de cifrado.
#
# SQLite no se instala: el proveedor de .NET trae la librería adentro y la base es un archivo.

set -euo pipefail

VERSION_DEFAULT="latest"
REPO="guidozanon/easyrest"
SERVICE="easyrest-sync"
USER_NAME="easyrest"

PREFIX="${EASYREST_PREFIX:-}"
VERSION="${EASYREST_VERSION:-$VERSION_DEFAULT}"
TARBALL="${EASYREST_TARBALL:-}"
PUBLIC_URL="${EASYREST_PUBLIC_URL:-}"
PORT="${EASYREST_PORT:-8080}"
PROVIDER_KIND="${EASYREST_PROVIDER_KIND:-}"
PROVIDER_ID="${EASYREST_PROVIDER_ID:-}"
PROVIDER_NAME="${EASYREST_PROVIDER_NAME:-}"
AUTHORITY="${EASYREST_AUTHORITY:-}"
CLIENT_ID="${EASYREST_CLIENT_ID:-}"
CLIENT_SECRET="${EASYREST_CLIENT_SECRET:-}"
ADMIN_EMAILS="${EASYREST_ADMIN_EMAILS:-}"
EMAIL_DOMAINS="${EASYREST_EMAIL_DOMAINS:-}"
UNATTENDED="${EASYREST_UNATTENDED:-0}"
NO_SYSTEMD="${EASYREST_NO_SYSTEMD:-0}"
ACTION="install"
PURGE=0

usage() {
  cat <<'FIN'
Uso: install.sh [opciones]

  --version X.Y.Z        versión a instalar (default: la última release)
  --tarball RUTA         instalar desde un tarball local en vez de descargarlo
  --url URL              URL pública del server (https://sync.empresa.com)
  --port N               puerto donde escucha (default 8080)
  --provider TIPO        oidc | github | ninguno
  --provider-id ID       identificador corto (google, entra, okta…)
  --provider-name NOMBRE nombre que ve la gente en el botón de login
  --authority URL        sólo para oidc: la autoridad con /.well-known/openid-configuration
  --client-id ID
  --client-secret SECRET
  --admin-email MAIL     administrador del server (se puede repetir)
  --email-domain DOM     dominio de mail permitido (se puede repetir)
  --unattended           no preguntar nada: falla si falta algo obligatorio
  --uninstall            desinstalar (conserva datos y config)
  --purge                con --uninstall, borra también la base y la configuración
  --prefix DIR           raíz alternativa, para pruebas
  -h, --help

Todas las opciones tienen su variable de entorno equivalente (EASYREST_URL → --url, etc.),
para automatizar con Ansible o cloud-init.
FIN
}

while [ $# -gt 0 ]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --tarball) TARBALL="$2"; shift 2 ;;
    --url) PUBLIC_URL="$2"; shift 2 ;;
    --port) PORT="$2"; shift 2 ;;
    --provider) PROVIDER_KIND="$2"; shift 2 ;;
    --provider-id) PROVIDER_ID="$2"; shift 2 ;;
    --provider-name) PROVIDER_NAME="$2"; shift 2 ;;
    --authority) AUTHORITY="$2"; shift 2 ;;
    --client-id) CLIENT_ID="$2"; shift 2 ;;
    --client-secret) CLIENT_SECRET="$2"; shift 2 ;;
    --admin-email) ADMIN_EMAILS="${ADMIN_EMAILS:+$ADMIN_EMAILS,}$2"; shift 2 ;;
    --email-domain) EMAIL_DOMAINS="${EMAIL_DOMAINS:+$EMAIL_DOMAINS,}$2"; shift 2 ;;
    --unattended) UNATTENDED=1; shift ;;
    --uninstall) ACTION="uninstall"; shift ;;
    --purge) PURGE=1; shift ;;
    --prefix) PREFIX="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Opción desconocida: $1" >&2; usage >&2; exit 2 ;;
  esac
done

APP_DIR="$PREFIX/opt/easyrest-sync"
CONF_DIR="$PREFIX/etc/easyrest-sync"
CONF_FILE="$CONF_DIR/server.env"
DATA_DIR="$PREFIX/var/lib/easyrest-sync"
UNIT_FILE="$PREFIX/etc/systemd/system/$SERVICE.service"

# ---------- salida ----------

if [ -t 1 ]; then
  B=$(printf '\033[1m'); G=$(printf '\033[32m'); Y=$(printf '\033[33m')
  R=$(printf '\033[31m'); N=$(printf '\033[0m')
else
  B=""; G=""; Y=""; R=""; N=""
fi

log()  { echo "${B}==>${N} $*"; }
ok()   { echo "  ${G}✔${N} $*"; }
warn() { echo "  ${Y}!${N} $*"; }
die()  { echo "${R}Error:${N} $*" >&2; exit 1; }

ask() {
  # ask <variable> <pregunta> [default]
  local __var="$1" __prompt="$2" __default="${3:-}" __answer=""
  local __current="${!__var:-}"

  [ -n "$__current" ] && return 0                       # ya vino por flag o env
  if [ "$UNATTENDED" = "1" ] || [ ! -t 0 ]; then
    [ -n "$__default" ] && printf -v "$__var" '%s' "$__default"
    return 0
  fi

  if [ -n "$__default" ]; then
    read -r -p "$__prompt [$__default]: " __answer
    __answer="${__answer:-$__default}"
  else
    read -r -p "$__prompt: " __answer
  fi
  printf -v "$__var" '%s' "$__answer"
}

systemd_available() {
  [ "$NO_SYSTEMD" != "1" ] && [ -z "$PREFIX" ] && command -v systemctl >/dev/null 2>&1 \
    && [ -d /run/systemd/system ]
}

# ---------- desinstalar ----------

uninstall() {
  log "Desinstalando EasyRest Sync"

  if systemd_available; then
    systemctl disable --now "$SERVICE" >/dev/null 2>&1 || true
    rm -f "$UNIT_FILE"
    systemctl daemon-reload || true
    ok "servicio detenido y removido"
  fi

  rm -rf "$APP_DIR"
  ok "binarios borrados"

  if [ "$PURGE" = "1" ]; then
    rm -rf "$DATA_DIR" "$CONF_DIR"
    ok "datos y configuración borrados"
    warn "la clave de cifrado se fue con la configuración: los secretos que hubiera ya no se pueden recuperar"
  else
    echo
    warn "se conservaron los datos en $DATA_DIR y la configuración en $CONF_FILE"
    warn "para borrarlos también: install.sh --uninstall --purge"
  fi
  exit 0
}

[ "$ACTION" = "uninstall" ] && uninstall

# ---------- chequeos ----------

[ -z "$PREFIX" ] && [ "$(id -u)" != "0" ] && die "hay que correrlo como root (probá con sudo)."

case "$(uname -m)" in
  x86_64|amd64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) die "arquitectura no soportada: $(uname -m). Sólo hay binarios para x86_64 y arm64." ;;
esac

command -v tar >/dev/null 2>&1 || die "falta tar."
UPGRADE=0
[ -f "$CONF_FILE" ] && UPGRADE=1

if [ "$UPGRADE" = "1" ]; then
  log "Actualizando una instalación existente"
  ok "se conservan la configuración y la clave de cifrado de $CONF_FILE"
else
  log "Instalando EasyRest Sync"
fi

# ---------- obtener el binario ----------

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [ -n "$TARBALL" ]; then
  [ -f "$TARBALL" ] || die "no existe el tarball $TARBALL"
  cp "$TARBALL" "$WORK/easyrest-sync.tar.gz"
  ok "usando $TARBALL"
else
  if [ "$VERSION" = "latest" ]; then
    BASE="https://github.com/$REPO/releases/latest/download"
  else
    BASE="https://github.com/$REPO/releases/download/v${VERSION#v}"
  fi

  fetch() {
    if command -v curl >/dev/null 2>&1; then curl -fsSL "$1" -o "$2"
    elif command -v wget >/dev/null 2>&1; then wget -qO "$2" "$1"
    else die "hace falta curl o wget para descargar."; fi
  }

  log "Descargando easyrest-sync-$RID.tar.gz ($VERSION)"
  fetch "$BASE/easyrest-sync-$RID.tar.gz" "$WORK/easyrest-sync.tar.gz" \
    || die "no se pudo descargar el paquete. ¿Existe esa versión para $RID?"

  # el hash se publica al lado del tarball: si no coincide, no se toca nada
  if fetch "$BASE/easyrest-sync-$RID.tar.gz.sha256" "$WORK/esperado.sha256" 2>/dev/null; then
    if command -v sha256sum >/dev/null 2>&1; then
      ACTUAL="$(sha256sum "$WORK/easyrest-sync.tar.gz" | cut -d' ' -f1)"
      EXPECTED="$(tr -d '[:space:]' < "$WORK/esperado.sha256")"
      [ "$ACTUAL" = "$EXPECTED" ] || die "el sha256 no coincide: descarga corrupta o manipulada."
      ok "sha256 verificado"
    else
      warn "no está sha256sum: no se pudo verificar la descarga"
    fi
  else
    warn "la release no publica el .sha256: no se pudo verificar la descarga"
  fi
fi

mkdir -p "$WORK/extract"
tar -xzf "$WORK/easyrest-sync.tar.gz" -C "$WORK/extract"
BIN_SRC="$(dirname "$(find "$WORK/extract" -name EasyRest.Sync.Server -type f | head -1)")"
[ -n "$BIN_SRC" ] && [ -d "$BIN_SRC" ] || die "el paquete no trae EasyRest.Sync.Server."

# ---------- usuario y carpetas ----------

if [ -z "$PREFIX" ]; then
  if ! id "$USER_NAME" >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "$USER_NAME" 2>/dev/null \
      || useradd --system --no-create-home --shell /sbin/nologin "$USER_NAME"
    ok "usuario de sistema '$USER_NAME' creado"
  fi
fi

mkdir -p "$APP_DIR" "$CONF_DIR" "$DATA_DIR"

# ---------- configuración ----------

generate_key() {
  if command -v openssl >/dev/null 2>&1; then openssl rand -base64 32
  else head -c 32 /dev/urandom | base64 | tr -d '\n'; fi
}

if [ "$UPGRADE" = "0" ]; then
  echo
  ask PUBLIC_URL "URL pública del server (la que va a usar la gente)" "http://localhost:$PORT"
  ask PORT "Puerto donde escucha" "$PORT"

  echo
  echo "Proveedor de identidad. Google, Microsoft Entra, Okta, Keycloak y Authentik entran"
  echo "todos por 'oidc'; GitHub va aparte porque no implementa OIDC."
  ask PROVIDER_KIND "Tipo (oidc / github / ninguno)" "ninguno"

  case "$PROVIDER_KIND" in
    oidc)
      ask PROVIDER_ID "Identificador corto" "oidc"
      ask PROVIDER_NAME "Nombre en el botón de login" "Iniciar sesión"
      ask AUTHORITY "Authority del IdP (ej. https://accounts.google.com)"
      ask CLIENT_ID "Client ID"
      ask CLIENT_SECRET "Client secret"
      ;;
    github)
      ask PROVIDER_ID "Identificador corto" "github"
      ask PROVIDER_NAME "Nombre en el botón de login" "GitHub"
      ask CLIENT_ID "Client ID"
      ask CLIENT_SECRET "Client secret"
      ;;
    *) PROVIDER_KIND="" ;;
  esac

  echo
  ask ADMIN_EMAILS "Mails de administradores del server, separados por coma (vacío = el primero que entre)" ""
  ask EMAIL_DOMAINS "Dominios de mail permitidos, separados por coma (vacío = todos)" ""

  if [ "$UNATTENDED" = "1" ]; then
    [ -n "$PUBLIC_URL" ] || die "en modo desatendido hace falta --url."
    if [ -n "$PROVIDER_KIND" ]; then
      [ -n "$CLIENT_ID" ] && [ -n "$CLIENT_SECRET" ] || die "el provider necesita --client-id y --client-secret."
      [ "$PROVIDER_KIND" = "oidc" ] && [ -z "$AUTHORITY" ] && die "un provider oidc necesita --authority."
    fi
  fi

  MASTER_KEY="$(generate_key)"

  {
    echo "# Configuración del servidor de sync de EasyRest."
    echo "# Generado por install.sh. Se conserva al actualizar."
    echo
    echo "# ¡GUARDÁ UNA COPIA DE ESTA CLAVE! Cifra los secretos de todos los workspaces:"
    echo "# si la perdés, los secretos guardados no se pueden recuperar."
    echo "EASYREST_MASTER_KEY=$MASTER_KEY"
    echo
    echo "ASPNETCORE_URLS=http://127.0.0.1:$PORT"
    echo "Database__Provider=sqlite"
    echo "ConnectionStrings__Default=Data Source=/var/lib/easyrest-sync/easyrest-sync.db"
    echo
    echo "Auth__PublicUrl=$PUBLIC_URL"
    echo "Auth__AllowedRedirectSchemes__0=easyrest"

    if [ -n "$PROVIDER_KIND" ]; then
      echo
      echo "Auth__Providers__0__Id=$PROVIDER_ID"
      echo "Auth__Providers__0__DisplayName=$PROVIDER_NAME"
      echo "Auth__Providers__0__Kind=$PROVIDER_KIND"
      [ "$PROVIDER_KIND" = "oidc" ] && echo "Auth__Providers__0__Authority=$AUTHORITY"
      echo "Auth__Providers__0__ClientId=$CLIENT_ID"
      echo "Auth__Providers__0__ClientSecret=$CLIENT_SECRET"
    fi

    if [ -n "$ADMIN_EMAILS" ]; then
      echo
      i=0
      IFS=',' read -ra _mails <<< "$ADMIN_EMAILS"
      for mail in "${_mails[@]}"; do
        mail="$(echo "$mail" | xargs)"
        [ -n "$mail" ] && echo "Auth__ServerAdminEmails__${i}=$mail" && i=$((i+1))
      done
    fi

    if [ -n "$EMAIL_DOMAINS" ]; then
      echo
      i=0
      IFS=',' read -ra _doms <<< "$EMAIL_DOMAINS"
      for dom in "${_doms[@]}"; do
        dom="$(echo "$dom" | xargs)"
        [ -n "$dom" ] && echo "Auth__AllowedEmailDomains__${i}=$dom" && i=$((i+1))
      done
    fi
  } > "$CONF_FILE"

  ok "configuración escrita en $CONF_FILE"
  ok "clave de cifrado generada"
fi

chmod 640 "$CONF_FILE"
[ -z "$PREFIX" ] && chown "root:$USER_NAME" "$CONF_FILE"

# ---------- archivos ----------

rm -rf "$APP_DIR.old"
[ -d "$APP_DIR" ] && [ -n "$(ls -A "$APP_DIR" 2>/dev/null)" ] && mv "$APP_DIR" "$APP_DIR.old"
mkdir -p "$APP_DIR"
cp -R "$BIN_SRC/." "$APP_DIR/"
chmod +x "$APP_DIR/EasyRest.Sync.Server"
rm -rf "$APP_DIR.old"

if [ -z "$PREFIX" ]; then
  chown -R root:root "$APP_DIR"
  chown -R "$USER_NAME:$USER_NAME" "$DATA_DIR"
fi
chmod 700 "$DATA_DIR"
ok "binarios instalados en $APP_DIR"

# ---------- servicio ----------

mkdir -p "$(dirname "$UNIT_FILE")"
cat > "$UNIT_FILE" <<FIN
[Unit]
Description=EasyRest Sync Server
Documentation=https://github.com/$REPO/blob/main/docs/SYNC.md
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$USER_NAME
Group=$USER_NAME
WorkingDirectory=/opt/easyrest-sync
EnvironmentFile=/etc/easyrest-sync/server.env
ExecStart=/opt/easyrest-sync/EasyRest.Sync.Server
Restart=on-failure
RestartSec=5

# El servicio guarda secretos: conviene que no pueda hacer mucho más que lo suyo.
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/var/lib/easyrest-sync
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictNamespaces=yes
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
LockPersonality=yes

# NO poner esto en yes: el JIT de .NET necesita memoria ejecutable y el servicio no arranca.
MemoryDenyWriteExecute=no

[Install]
WantedBy=multi-user.target
FIN
ok "unit de systemd escrito en $UNIT_FILE"

if systemd_available; then
  systemctl daemon-reload
  systemctl enable "$SERVICE" >/dev/null 2>&1 || true
  systemctl restart "$SERVICE"

  log "Esperando a que responda"
  HEALTHY=0
  for _ in $(seq 1 30); do
    if command -v curl >/dev/null 2>&1 &&
       curl -fsS "http://127.0.0.1:$PORT/health" >/dev/null 2>&1; then HEALTHY=1; break; fi
    sleep 1
  done

  if [ "$HEALTHY" = "1" ]; then
    ok "el servicio está arriba"
  else
    warn "no respondió todavía. Mirá qué pasó con:  journalctl -u $SERVICE -n 50"
  fi
else
  warn "no hay systemd disponible: el servicio quedó instalado pero no se arrancó"
fi

# ---------- resumen ----------

CALLBACK="${PUBLIC_URL%/}/api/v1/auth/callback"
echo
log "Listo"
echo
echo "  Consola:   ${PUBLIC_URL%/}/Admin"
echo "  Config:    $CONF_FILE"
echo "  Datos:     $DATA_DIR"
echo "  Servicio:  systemctl status $SERVICE"
echo "  Logs:      journalctl -u $SERVICE -f"

if [ "$UPGRADE" = "0" ]; then
  echo
  echo "${B}Registrá este redirect en tu proveedor de identidad:${N}"
  echo "  $CALLBACK"

  case "$PUBLIC_URL" in
    https://*) ;;
    http://localhost*|http://127.0.0.1*) ;;
    *)
      echo
      warn "la URL pública no es https: la mayoría de los IdP rechazan redirects http que no sean"
      warn "localhost. Poné el server detrás de un proxy con TLS (nginx, Caddy) antes de usarlo."
      ;;
  esac

  echo
  echo "${B}Guardá una copia de la clave de cifrado:${N}"
  echo "  grep EASYREST_MASTER_KEY $CONF_FILE"
  echo "  Sin esa clave, una restauración de backup no puede leer los secretos."
fi
echo
