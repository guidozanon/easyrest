#!/usr/bin/env bash
#
# Tests del instalador. Corren con --prefix contra un directorio temporal, así que no tocan
# nada del sistema ni necesitan systemd: verifican lo que el script escribe y, sobre todo, que
# actualizar no pise la clave de cifrado — perderla es perder los secretos de todos.
#
#   bash tests/install/test-install.sh

set -uo pipefail

INSTALLER="$(cd "$(dirname "$0")/../.." && pwd)/install/install.sh"
PASSED=0
FAILED=0

ok()   { echo "  ✔ $1"; PASSED=$((PASSED+1)); }
fail() { echo "  ✘ $1"; echo "      $2"; FAILED=$((FAILED+1)); }

assert_contains() {
  # assert_contains <archivo> <patrón> <descripción>
  if grep -qF -- "$2" "$1" 2>/dev/null; then ok "$3"
  else fail "$3" "no se encontró '$2' en $1"; fi
}

assert_missing() {
  if [ ! -e "$1" ]; then ok "$2"; else fail "$2" "$1 debería no existir"; fi
}

assert_exists() {
  if [ -e "$1" ]; then ok "$2"; else fail "$2" "falta $1"; fi
}

assert_equal() {
  if [ "$1" = "$2" ]; then ok "$3"; else fail "$3" "esperaba '$2' y vino '$1'"; fi
}

# Tarball de mentira con la forma que arma el CI: easyrest-sync/EasyRest.Sync.Server
make_tarball() {
  local dir="$1/paquete"
  mkdir -p "$dir/easyrest-sync"
  echo '#!/bin/sh' > "$dir/easyrest-sync/EasyRest.Sync.Server"
  echo 'echo server' >> "$dir/easyrest-sync/EasyRest.Sync.Server"
  echo '{}' > "$dir/easyrest-sync/appsettings.json"
  tar -czf "$1/easyrest-sync.tar.gz" -C "$dir" easyrest-sync
  echo "$1/easyrest-sync.tar.gz"
}

run_installer() {
  # run_installer <prefix> <tarball> [args...]
  local prefix="$1" tarball="$2"; shift 2
  EASYREST_NO_SYSTEMD=1 bash "$INSTALLER" \
    --prefix "$prefix" --tarball "$tarball" --unattended "$@" > "$prefix/salida.log" 2>&1
}

echo "== Instalación limpia"
ROOT="$(mktemp -d)"
TARBALL="$(make_tarball "$ROOT")"
PREFIX="$ROOT/sistema"; mkdir -p "$PREFIX"

if run_installer "$PREFIX" "$TARBALL" \
     --url https://sync.empresa.com --port 9099 \
     --provider oidc --provider-id google --provider-name Google \
     --authority https://accounts.google.com \
     --client-id abc --client-secret shh \
     --admin-email uno@empresa.com --admin-email dos@empresa.com \
     --email-domain empresa.com; then
  ok "el instalador terminó bien"
else
  fail "el instalador terminó bien" "$(tail -5 "$PREFIX/salida.log")"
fi

CONF="$PREFIX/etc/easyrest-sync/server.env"
assert_exists "$PREFIX/opt/easyrest-sync/EasyRest.Sync.Server" "instala el binario"
assert_exists "$CONF" "escribe la configuración"
assert_exists "$PREFIX/var/lib/easyrest-sync" "crea el directorio de datos"
assert_exists "$PREFIX/etc/systemd/system/easyrest-sync.service" "escribe el unit de systemd"

assert_contains "$CONF" "EASYREST_MASTER_KEY=" "genera la clave de cifrado"
assert_contains "$CONF" "Auth__PublicUrl=https://sync.empresa.com" "guarda la URL pública"
assert_contains "$CONF" "ASPNETCORE_URLS=http://127.0.0.1:9099" "usa el puerto indicado"
assert_contains "$CONF" "Auth__Providers__0__Kind=oidc" "configura el provider"
assert_contains "$CONF" "Auth__Providers__0__Authority=https://accounts.google.com" "guarda la authority"
assert_contains "$CONF" "Auth__ServerAdminEmails__0=uno@empresa.com" "primer admin"
assert_contains "$CONF" "Auth__ServerAdminEmails__1=dos@empresa.com" "segundo admin, indexado"
assert_contains "$CONF" "Auth__AllowedEmailDomains__0=empresa.com" "dominio permitido"

assert_equal "$(stat -c '%a' "$CONF")" "640" "la config no la puede leer cualquiera"

UNIT="$PREFIX/etc/systemd/system/easyrest-sync.service"
assert_contains "$UNIT" "MemoryDenyWriteExecute=no" "deja el JIT de .NET poder arrancar"
assert_contains "$UNIT" "ProtectSystem=strict" "endurece el servicio"
assert_contains "$UNIT" "ReadWritePaths=/var/lib/easyrest-sync" "sólo puede escribir en sus datos"
assert_contains "$UNIT" "User=easyrest" "no corre como root"

echo
echo "== Actualización sobre una instalación existente"
CLAVE_ANTES="$(grep '^EASYREST_MASTER_KEY=' "$CONF")"
echo "# marca de agua" >> "$CONF"

if run_installer "$PREFIX" "$TARBALL" --url https://otra-cosa.example; then
  ok "la actualización terminó bien"
else
  fail "la actualización terminó bien" "$(tail -5 "$PREFIX/salida.log")"
fi

CLAVE_DESPUES="$(grep '^EASYREST_MASTER_KEY=' "$CONF")"
assert_equal "$CLAVE_DESPUES" "$CLAVE_ANTES" "NO regenera la clave de cifrado al actualizar"
assert_contains "$CONF" "# marca de agua" "no pisa la configuración existente"
assert_contains "$CONF" "Auth__PublicUrl=https://sync.empresa.com" "ignora los flags al actualizar"
assert_exists "$PREFIX/opt/easyrest-sync/EasyRest.Sync.Server" "reemplaza los binarios"
assert_missing "$PREFIX/opt/easyrest-sync.old" "no deja la instalación anterior tirada"

echo
echo "== Desatendido sin datos obligatorios"
PREFIX2="$ROOT/sistema2"; mkdir -p "$PREFIX2"
if run_installer "$PREFIX2" "$TARBALL" --provider oidc --client-id x --client-secret y; then
  fail "falla si falta --url en modo desatendido" "terminó bien y no debería"
else
  ok "falla si falta --url en modo desatendido"
fi

PREFIX3="$ROOT/sistema3"; mkdir -p "$PREFIX3"
if run_installer "$PREFIX3" "$TARBALL" --url https://x.example --provider oidc --client-id x --client-secret y; then
  fail "un provider oidc sin authority no pasa" "terminó bien y no debería"
else
  ok "un provider oidc sin authority no pasa"
fi

echo
echo "== Sin proveedor de identidad"
PREFIX4="$ROOT/sistema4"; mkdir -p "$PREFIX4"
run_installer "$PREFIX4" "$TARBALL" --url https://sinidp.example
CONF4="$PREFIX4/etc/easyrest-sync/server.env"
assert_contains "$CONF4" "EASYREST_MASTER_KEY=" "instala igual sin provider"
if grep -q "Auth__Providers__0__" "$CONF4"; then
  fail "no inventa un provider" "escribió configuración de provider"
else
  ok "no inventa un provider"
fi

echo
echo "== Tarball inexistente"
PREFIX5="$ROOT/sistema5"; mkdir -p "$PREFIX5"
if run_installer "$PREFIX5" "$ROOT/no-existe.tar.gz" --url https://x.example; then
  fail "falla con un tarball que no existe" "terminó bien y no debería"
else
  ok "falla con un tarball que no existe"
fi
assert_missing "$PREFIX5/etc/easyrest-sync/server.env" "no deja configuración a medio escribir"

echo
echo "== Desinstalación"
EASYREST_NO_SYSTEMD=1 bash "$INSTALLER" --prefix "$PREFIX" --uninstall > "$PREFIX/desinstalar.log" 2>&1
assert_missing "$PREFIX/opt/easyrest-sync" "borra los binarios"
assert_exists "$CONF" "conserva la configuración"
assert_exists "$PREFIX/var/lib/easyrest-sync" "conserva los datos"

EASYREST_NO_SYSTEMD=1 bash "$INSTALLER" --prefix "$PREFIX" --uninstall --purge > "$PREFIX/purgar.log" 2>&1
assert_missing "$CONF" "con --purge borra la configuración"
assert_missing "$PREFIX/var/lib/easyrest-sync" "con --purge borra los datos"

rm -rf "$ROOT"

echo
echo "-----------------------------------------"
echo "  $PASSED pasaron, $FAILED fallaron"
[ "$FAILED" -eq 0 ] || exit 1
