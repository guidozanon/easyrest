# Firma de código: macOS y Windows

Los binarios de EasyRest los firma el workflow `.github/workflows/build.yml`. Los pasos de firma
**se prenden solos cuando están cargados los secrets** y se saltean si no están, así que el CI
sigue funcionando sin credenciales (publica sin firmar, como antes de esto).

| Plataforma | Qué se firma | Con qué | Costo |
|---|---|---|---|
| macOS | `EasyRest.app` (firma + notarización) | Apple Developer ID | USD 99/año (cuenta de Apple Developer) |
| Windows | `EasyRest.exe` y `EasyRest-Setup-x.y.z.exe` | SignPath Foundation | gratis para proyectos open source |

---

## macOS: Developer ID + notarización

Firmar con Developer ID **y notarizar** es lo único que hace que Gatekeeper abra la app sin
ningún cartel. Con firma ad-hoc (lo que hacía el CI antes) el sistema la bloquea la primera vez, y
desde macOS 15 Sequoia ya no alcanza con click derecho → Abrir: hay que ir a *Ajustes del Sistema →
Privacidad y seguridad → Abrir de todos modos*.

### 1. Certificado

1. En [developer.apple.com](https://developer.apple.com/account/resources/certificates/list) →
   *Certificates* → **+** → tipo **Developer ID Application**.
   - Es el tipo para distribuir fuera del App Store. No sirven "Apple Development" ni
     "Mac App Distribution".
   - Necesita un CSR: en la Mac, *Acceso a Llaveros → Asistente para certificados → Solicitar un
     certificado de una autoridad certificadora*, guardándolo en disco.
2. Bajá el `.cer` y hacele doble click para importarlo al llavero.
3. En *Acceso a Llaveros*, buscá **Developer ID Application: … (TEAMID)**, click derecho →
   *Exportar* → formato `.p12`, con contraseña (esa contraseña es el secret `MACOS_CERT_PASSWORD`).
   Asegurate de exportar el certificado **con su clave privada** (tiene que aparecer la flechita
   para desplegarlo).
4. Pasá el `.p12` a base64 en una línea:

   ```bash
   base64 -i EasyRest-DeveloperID.p12 | tr -d '\n' | pbcopy
   ```

### 2. Contraseña para notarizar

`notarytool` no acepta la contraseña de tu Apple ID: necesita una **app-specific password**.

1. [account.apple.com](https://account.apple.com) → *Iniciar sesión y seguridad* →
   *Contraseñas específicas para apps* → **+** → nombre: `EasyRest notarization`.
2. Copiá la contraseña (formato `xxxx-xxxx-xxxx-xxxx`).
3. El **Team ID** son los 10 caracteres que aparecen en developer.apple.com → *Membership*, y
   también entre paréntesis en el nombre del certificado.

### 3. Secrets en GitHub

*Settings → Secrets and variables → Actions → New repository secret*:

| Secret | Valor |
|---|---|
| `MACOS_CERT_P12` | el `.p12` en base64 (paso 1.4) |
| `MACOS_CERT_PASSWORD` | la contraseña con la que exportaste el `.p12` |
| `APPLE_ID` | el mail de la cuenta de Apple Developer |
| `APPLE_TEAM_ID` | el Team ID de 10 caracteres |
| `APPLE_APP_PASSWORD` | la app-specific password (paso 2) |

Con `MACOS_CERT_P12` cargado, el job hace: importar el certificado en un llavero temporal de la
corrida → firmar todos los Mach-O internos y después el bundle con
[hardened runtime](../build/EasyRest.entitlements) → `notarytool submit --wait` → `stapler staple`
→ `spctl -a -vvv -t exec` como verificación final.

### Notas

- Los **entitlements** (`build/EasyRest.entitlements`) no son opcionales: el hardened runtime
  mata al CLR si no se le permite JIT y memoria ejecutable. Si la app abre en tu Mac pero crashea
  al arrancar en otra después de notarizar, empezá mirando ahí.
- La primera notarización de una cuenta nueva puede tardar bastante; después suele ser un par de
  minutos. El paso tiene `--timeout 30m`.
- Si Apple rechaza el envío, el log completo se saca con
  `xcrun notarytool log <submission-id> --apple-id … --team-id … --password …`.
- El certificado vence a los 5 años y la membresía se renueva por año: si no la renovás, lo ya
  notarizado sigue abriendo, pero no podés firmar releases nuevas.

---

## Windows: SignPath Foundation

[SignPath Foundation](https://signpath.org) da firma de código gratis a proyectos open source: el
certificado (OV, emitido por Sectigo) queda a nombre de la Foundation, que verifica que el binario
salió del repo público. No hay que comprar ni guardar ninguna clave.

### 1. Requisitos (revisar antes de aplicar)

EasyRest los cumple, pero conviene chequearlos porque son la causa habitual de rechazo:

- [x] Licencia **aprobada por la OSI**, sin dual-licensing comercial → EasyRest es MIT.
- [x] Código en un **repositorio público** y descargas gratuitas.
- [x] Sin componentes propietarios ni de código cerrado.
- [x] Sin malware ni PUP.
- [x] **Mantenido activamente** (commits y releases recientes).
- [x] **Ya publicado en la forma que se va a firmar**: tiene que haber releases con los binarios
      de Windows antes de aplicar. Ya hay hasta la v0.1.10.
- [x] La **funcionalidad descrita en la página de descarga** → el README explica qué hace.

### 2. Aplicar

Se entra por **<https://signpath.io/solutions/open-source-community>** (las condiciones completas
están en <https://signpath.org/terms.html>). Lo que pide el formulario, con lo que corresponde a
EasyRest:

| Campo | Qué poner |
|---|---|
| Project name | `EasyRest` |
| Repository URL | `https://github.com/guidozanon/easyrest` |
| License | MIT |
| Description | Cliente HTTP de escritorio (estilo Postman/Bruno) en .NET 8 + Avalonia, con colecciones, ambientes, import de OpenAPI, scripts y runner de carga. Multiplataforma (Windows/macOS/Linux). |
| Download page | la página de Releases del repo |
| Build system | GitHub Actions (`.github/workflows/build.yml`) |
| Qué se firma | el ejecutable `EasyRest.exe` y el installer `EasyRest-Setup-<versión>.exe` |
| Maintainer | tu nombre y mail de contacto |

La revisión es manual: desde unos días hasta un par de semanas.

### 3. Configurar el proyecto en SignPath (post-aprobación)

En el dashboard de SignPath, dentro de la organización que te asignen:

1. **Project** con slug `easyrest`.
2. Dos **artifact configurations**, con el XML que ya está en el repo:
   - slug `exe` → [`.signpath/artifact-configurations/exe.xml`](../.signpath/artifact-configurations/exe.xml)
   - slug `installer` → [`.signpath/artifact-configurations/installer.xml`](../.signpath/artifact-configurations/installer.xml)
3. Un **trusted build system** de tipo *GitHub* apuntando a:
   - repositorio `guidozanon/easyrest`
   - workflow `.github/workflows/build.yml`
   - tags `v*` (los releases salen de tags)

   El connector de GitHub verifica que el build lo hizo realmente ese workflow y que el artefacto
   se subió como workflow artifact antes de pedir la firma — por eso el workflow hace
   `upload-artifact` y recién después manda a firmar.
4. Una **signing policy** con slug `release-signing`, con origin verification prendida.

Si preferís otros slugs, no hace falta tocar el YAML: se pisan con *repository variables*
(`SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_EXE_CONFIG_SLUG`,
`SIGNPATH_INSTALLER_CONFIG_SLUG`).

### 4. Secrets en GitHub

| Secret | Valor |
|---|---|
| `SIGNPATH_API_TOKEN` | el token que emite SignPath para el proyecto |
| `SIGNPATH_ORGANIZATION_ID` | el UUID de la organización en SignPath |

Con eso cargado, el workflow firma el `EasyRest.exe`, arma el installer y el zip portable con el
exe **ya firmado**, y después manda a firmar el installer.

### Sobre SmartScreen

Un certificado OV nuevo **no apaga SmartScreen del día uno**: la reputación se acumula con
descargas del mismo publisher. Lo que baja el ruido desde el principio:

- firmar siempre con el mismo certificado y publicar el installer como descarga principal;
- **no** volver al `.exe` single-file (ver abajo);
- si en algún momento hace falta cero warnings inmediato, la única vía es un certificado **EV**
  (USD 300-600/año) o [Azure Trusted Signing](https://azure.microsoft.com/pricing/details/trusted-signing/)
  (USD 9.99/mes) — ambos pagos, y ninguno reemplaza lo de arriba.

---

## Empaquetado de Windows: por qué ya no hay un único .exe

Hasta la v0.1.10 el CI publicaba `EasyRest-windows-x64.zip` con un `.exe` single-file
autocontenido. Eso es lo peor para los antivirus: el bundle se **auto-extrae en `%TEMP%` en cada
arranque** y ejecuta desde ahí, que es el patrón que buscan las heurísticas (y no hay forma de
firmar lo que se extrae).

Ahora se publica:

| Asset | Para qué |
|---|---|
| `EasyRest-Setup-<versión>.exe` | descarga recomendada. Instala en `%LocalAppData%\Programs\EasyRest` sin UAC, con acceso directo y desinstalador |
| `EasyRest-win-x64-portable.zip` | la misma carpeta autocontenida, sin instalar. Es el asset que consume el auto update |

El installer lo compila Inno Setup ([`build/easyrest.iss`](../build/easyrest.iss)). Instalar por
usuario, y no en `Program Files`, tiene un motivo concreto además de evitar el UAC: el auto update
reemplaza la carpeta de instalación, y ahí tiene permisos de escritura sin ser admin.

### Migración de los que ya tienen la versión single-file

El auto update de Windows ahora reemplaza **la carpeta**, no un archivo. Para que un install viejo
no se rompa solo, el zip portable **a propósito no se llama `…windows-x64.zip`**: las versiones
≤0.1.10 buscaban cualquier zip cuyo nombre contuviera ese texto y sabían reemplazar un único
`.exe`. Si les matcheara el zip nuevo, moverían `EasyRest.exe` sin sus DLLs y dejarían la
instalación inservible.

Con el nombre nuevo, una instalación vieja no encuentra asset, y el panel de actualizaciones le
dice que baje el installer una vez. Después de eso vuelve a actualizarse sola. Conviene aclararlo
en las notas del primer release firmado.
