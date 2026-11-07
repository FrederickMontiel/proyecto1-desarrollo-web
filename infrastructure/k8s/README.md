# Despliegue en Kubernetes / K3s

Guía paso a paso para levantar SWMATRC en **K3s**, en local (WSL) o en la VPS. La explicación
de la arquitectura está en [docs/FASE-4.md](../../docs/FASE-4.md).

| | Local (WSL) | Producción (VPS) |
|---|---|---|
| Dirección | `http://localhost` | `https://<su-dominio>` |
| TLS | no | Let's Encrypt (cert-manager) |
| Imágenes | construidas en la PC e importadas | publicadas por GitHub Actions en `ghcr.io` |
| Overlay | `overlays/local` | `overlays/produccion` |

Requisitos de memoria: unos **8 GB** libres (SQL Server sola pide 2 GB).

---

## 1. Instalar K3s

**WSL (Ubuntu 24.04 con systemd activo)**, desde PowerShell en la raíz del repositorio:

```powershell
wsl -d Ubuntu-24.04 -u root bash infrastructure/k8s/scripts/instalar-k3s.sh
```

**VPS**:

```bash
sudo CERT_MANAGER=1 bash infrastructure/k8s/scripts/instalar-k3s.sh
```

**VPS compartida** (ya hay nginx u otro proxy en 80/443 con otros sitios): Traefik va en
puertos alternos, configurados antes de arrancar K3s para que nunca tome el 80/443:

```bash
sudo TRAEFIK_PUERTO_HTTP=8081 TRAEFIK_PUERTO_HTTPS=8444 bash infrastructure/k8s/scripts/instalar-k3s.sh
```

Luego se agrega un sitio a ese nginx con
[nginx-k3s.conf.example](../nginx/nginx-k3s.conf.example) (certbot pone el HTTPS) y se
despliega con `desplegar.sh produccion-proxy`. No hace falta cert-manager.

El script instala K3s con el instalador oficial (`get.k3s.io`), espera a que el nodo esté listo,
ajusta Traefik para WebSocket y copia el kubeconfig a `~/.kube/config` del usuario. Con
`CERT_MANAGER=1` también instala cert-manager.

**Acceso desde Windows.** Traefik de K3s se publica con reglas de iptables dentro de WSL, y el
reenvío automático de `localhost` de WSL no las ve. Hay dos opciones:

- Sin permisos: entrar por la IP de WSL (`wsl -d Ubuntu-24.04 -- hostname -I`), p. ej.
  `http://172.21.244.236`. Cambia al reiniciar Windows.
- Recomendado: ejecutar **como administrador**
  `powershell -ExecutionPolicy Bypass -File infrastructure\k8s\scripts\publicar-wsl.ps1`.
  Publica el puerto 80 de Windows hacia K3s: el navegador entra por `http://localhost` (la URL
  de los callbacks de OAuth) y la Raspberry de la red local envía a `http://<IP-de-la-PC>`.
  Repetirlo después de reiniciar.

Para usar `kubectl` desde **Windows** contra el K3s de WSL (con la IP de WSL en lugar de
`127.0.0.1`):

```powershell
New-Item -ItemType Directory -Force $HOME\.kube | Out-Null
# Set-Content -Encoding ascii: con ">" PowerShell 5.1 escribe UTF-16 y kubectl no lo lee.
$ip = (wsl -d Ubuntu-24.04 -- hostname -I).Split(' ')[0]
(wsl -d Ubuntu-24.04 -- cat /etc/rancher/k3s/k3s.yaml) -replace '127.0.0.1', $ip | Set-Content -Encoding ascii $HOME\.kube\config
kubectl get nodes
```

---

## 2. Imágenes

**Local.** En Windows (Git Bash) se construyen y exportan las 8 imágenes; luego K3s las importa:

```bash
bash infrastructure/k8s/scripts/construir-imagenes.sh
```

```powershell
wsl -d Ubuntu-24.04 -u root bash infrastructure/k8s/scripts/importar-imagenes-k3s.sh
```

**Producción.** Al hacer push a `main`, el CI publica `ghcr.io/<usuario>/swmatrc-<servicio>:latest`.
Los paquetes de GHCR nacen privados: hágalos públicos (GitHub → *Packages* → *Settings* →
*Change visibility*) o cree un `imagePullSecret`.

---

## 3. Configuración y secretos

Todo sale de **`.env`** (copia de `.env.example`, no se versiona) y de
**`secrets/jwt-private.pem`** (se genera con `bash infrastructure/docker/generar-llaves.sh`).
`scripts/crear-secretos.sh` los convierte en Secrets de Kubernetes; `desplegar.sh` lo llama solo.

| Secret | Contenido |
|---|---|
| `swmatrc-db` | Contraseñas de PostgreSQL, SQL Server y MongoDB (una por servicio) |
| `swmatrc-jwt` | Llave privada RS256 (montada como archivo solo en Auth) |
| `swmatrc-oauth` | Client ID y Client Secret de Google y GitHub (montados como archivos) |
| `swmatrc-servicios` | Secretos de servicio a servicio |
| `swmatrc-admin` | Administrador inicial (solo con la base vacía) |
| `swmatrc-iot` | Llave de la Raspberry Pi para enviar lecturas reales |

Lo no sensible (URLs internas, dominio, banderas) está en el ConfigMap
[`base/configmap.yaml`](base/configmap.yaml).

---

## 4. Credenciales de Google y GitHub

Ambas son gratuitas. Un proveedor sin credenciales simplemente no muestra su botón.

**Google** — <https://console.cloud.google.com>

1. Crear un proyecto.
2. *Google Auth Platform* → **Branding** (nombre de la app y correo de soporte) → **Audience**:
   *External*. En modo *Testing*, agregar como *test users* los correos que van a entrar.
3. **Clients → Create client** → *Web application* → *Authorized redirect URIs*:
   - `http://localhost/api/auth/callback/google` (K3s local)
   - `http://localhost:10001/api/auth/callback/google` (Docker Compose)
   - `https://<su-dominio>/api/auth/callback/google` (producción)
4. Copiar el Client ID y el Client Secret.

**GitHub** — *Settings → Developer settings → OAuth Apps → New OAuth App*

1. *Homepage URL*: la dirección del entorno. *Authorization callback URL*:
   `<dirección>/api/auth/callback/github`.
2. *Register application* → copiar el Client ID → *Generate a new client secret*.
3. Cada OAuth App admite **una** URL de callback: cree una para local y otra para producción.

Ponga los valores en `.env`:

```env
URL_PUBLICA=http://localhost          # o https://<su-dominio>
OAUTH_GOOGLE_CLIENT_ID=...
OAUTH_GOOGLE_CLIENT_SECRET=...
OAUTH_GITHUB_CLIENT_ID=...
OAUTH_GITHUB_CLIENT_SECRET=...
```

En Kubernetes `URL_PUBLICA` la fija el overlay (ConfigMap), no el `.env`. Si cambia las
credenciales con el sistema ya desplegado:

```bash
bash infrastructure/k8s/scripts/crear-secretos.sh
kubectl -n swmatrc rollout restart deployment/auth-service
```

---

## 5. Desplegar

**Local:**

```bash
bash infrastructure/k8s/scripts/desplegar.sh local
```

**Producción** (el registro DNS `A` del dominio debe apuntar ya a la IP de la VPS, con los
puertos 80 y 443 abiertos):

```bash
DOMINIO=monitoreo.midominio.org CORREO_ACME=yo@midominio.org \
  bash infrastructure/k8s/scripts/desplegar.sh produccion
```

El script crea los Secrets, aplica el overlay y espera a que las bases, los microservicios, el
gateway y la web estén listos. El primer arranque tarda unos minutos (SQL Server y la JVM).

### Traer los datos de Docker Compose

Si los datos de la Fase 3 están en Docker Compose en la misma máquina (contenedores en marcha):

```bash
bash infrastructure/k8s/scripts/trasladar-datos.sh
```

Copia `auth_db`, `community_db`, `alert_db` (pg_dump/pg_restore), `sensor_db` (BACKUP/RESTORE)
y `report_db`, `audit_db` (mongodump/mongorestore), deteniendo los microservicios mientras
restaura.

---

## 6. Verificar

```bash
kubectl -n swmatrc get pods,svc,ingress,hpa,pvc
kubectl -n swmatrc get pods -o wide                          # réplicas repartidas
kubectl -n swmatrc logs deploy/auth-service | grep -i proveedor
curl -s http://localhost/api/auth/providers                  # [{"id":"google",…},{"id":"github",…}]
python tools/pruebas/e2e.py                                  # con GATEWAY=http://localhost
```

Demostraciones para la defensa:

| Qué | Cómo |
|---|---|
| Autorreparación | `kubectl -n swmatrc delete pod -l app.kubernetes.io/name=alert-service` → el Deployment crea otros |
| Persistencia | `kubectl -n swmatrc delete pod postgres-0` → vuelve con los mismos datos |
| Escalado | `kubectl -n swmatrc scale deploy/report-service --replicas=4` · `kubectl -n swmatrc get hpa` |
| Readiness | Con `mongo` caído, `report-service` deja de estar `READY` y no recibe tráfico, pero no se reinicia |
| Secretos | `kubectl -n swmatrc get secret swmatrc-oauth -o yaml` (base64, fuera del repositorio) |
| Rol | Un usuario VIEWER recibe 403 en `/api/audit/events` desde el gateway |

---

## 7. Problemas comunes

| Síntoma | Causa y solución |
|---|---|
| `ErrImageNeverPull` / `ImagePullBackOff` en local | Falta importar las imágenes (paso 2) o la etiqueta no es `fase4` |
| `sensor-service` reinicia al principio | Espera a que el Job `sqlserver-init` cree `sensor_db`: se recupera solo |
| `redirect_uri_mismatch` en Google | La URL de callback registrada no coincide letra por letra con `URL_PUBLICA` + `/api/auth/callback/google` |
| El botón de Google no aparece | Faltan `OAUTH_GOOGLE_CLIENT_ID/SECRET`: corra `crear-secretos.sh` y reinicie `auth-service` |
| El certificado no se emite | `kubectl -n swmatrc describe certificate swmatrc-tls`: el DNS debe resolver a la VPS y el puerto 80 debe estar abierto |
| El tablero se desconecta cada 60 s | Falta `k3s/traefik-config.yaml` (`kubectl apply -f infrastructure/k8s/k3s/traefik-config.yaml`) |
