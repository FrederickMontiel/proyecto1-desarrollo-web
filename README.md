# SWMATRC — Sistema Web de Monitoreo y Alerta Temprana para Riesgos Climáticos

Sistema distribuido que monitorea sensores ambientales en comunidades rurales, evalúa el riesgo
de inundación, tormenta, helada, sequía e incendio forestal, emite alertas en tiempo real y
genera reportes PDF.

**Arquitectura vigente (Fase 4):** microservicios en cuatro lenguajes y tres motores de base de
datos, autenticación con OAuth 2.0 / OpenID Connect (Google y GitHub) y despliegue en
Kubernetes/K3s.

| Documento | Contenido |
|---|---|
| [docs/FASE-4.md](docs/FASE-4.md) | **OAuth 2.0/OIDC, JWT, roles y Kubernetes/K3s** (fase actual) |
| [infrastructure/k8s/README.md](infrastructure/k8s/README.md) | Despliegue paso a paso en K3s (WSL y VPS) y credenciales de Google/GitHub |
| [docs/FASE-3.md](docs/FASE-3.md) | Microservicios, comunicación entre servicios y Docker Compose |
| [docs/FASE-2.md](docs/FASE-2.md) · [arquitectura](docs/FASE-2-arquitectura.md) | Sistema monolítico (histórico): requisitos funcionales, motor de riesgo y modelo de datos |
| [docs/FASE-1.md](docs/FASE-1.md) | Base del proyecto |

---

## Arquitectura

```text
Internet ─ DNS/HTTPS ─▶ Ingress (Traefik) ─┬─▶ API Gateway (.NET + YARP) ─┬─▶ Auth       NestJS        PostgreSQL
                                           │                               ├─▶ Community  .NET 10       PostgreSQL
                                           └─▶ web (Angular + nginx)       ├─▶ Sensor     .NET 10       SQL Server  (+ SignalR, Redis)
                                                                           ├─▶ Alert      Spring Boot   PostgreSQL
                                                                           ├─▶ Report     FastAPI       MongoDB
                                                                           └─▶ Audit      FastAPI       MongoDB
                                  Auth ◀──▶ Google / GitHub (OAuth 2.0 / OpenID Connect)
```

| Servicio | Lenguaje | Responsabilidad |
|---|---|---|
| `api-gateway` | C# | Punto de entrada; valida el JWT (firma, emisor, audiencia, vigencia, rol); límite de peticiones |
| `auth-service` | TypeScript | Usuarios, roles, permisos, login local, **OAuth/OIDC con Google y GitHub**, JWT RS256 + JWKS |
| `community-service` | C# | Comunidades y GeoJSON |
| `sensor-service` | C# | Sensores, lecturas, simulador, **sensores reales de la Raspberry Pi** (`/api/sensors/iot`) y tiempo real (SignalR) |
| `alert-service` | Java | Motor de riesgo, reglas, umbrales, alertas e historial |
| `report-service` | Python | 8 reportes PDF y dashboard estadístico |
| `audit-service` | Python | Bitácora centralizada |
| `web` | TypeScript | Frontend Angular |

### Roles

| Rol | Puede |
|---|---|
| `ADMIN` | Todo: usuarios, comunidades, sensores, reglas, alertas, reportes (incluida la descarga) y auditoría |
| `OPERATOR` | Operar sensores, atender alertas, corregir lecturas y generar reportes |
| `VIEWER` | Consultar el dashboard, alertas e historial. Es el rol de quien entra por primera vez con Google o GitHub |

---

## Estructura

```text
├── docs/                    FASE-1.md … FASE-4.md
├── frontend/                Angular 22
├── services/
│   ├── api-gateway/  auth-service/  community-service/  sensor-service/
│   ├── alert-service/  report-service/  audit-service/
│   └── dotnet-comun/        librería compartida de los servicios .NET
├── infrastructure/
│   ├── docker/              llaves JWT, respaldos, cortafuegos
│   ├── nginx/               proxy inverso para la VPS (alternativa sin Kubernetes)
│   └── k8s/                 Kustomize: base + overlays local/produccion, scripts
├── database/                postgres/  sqlserver/  mongodb/  (inicialización)
├── iot/raspberry/           estación de sensores reales (DHT11 + SW-420)
├── tools/                   migración de datos y pruebas de punta a punta
├── docker-compose.yml       entorno de desarrollo
└── .github/workflows/       CI
```

---

## Puesta en marcha

### Kubernetes / K3s (Fase 4)

```powershell
# 1. K3s en WSL (una vez)
wsl -d Ubuntu-24.04 -u root bash infrastructure/k8s/scripts/instalar-k3s.sh
```

```bash
# 2. Configuración: copiar y completar (contraseñas y, opcional, credenciales OAuth)
cp .env.example .env
bash infrastructure/docker/generar-llaves.sh

# 3. Imágenes (Git Bash) e importación a K3s
bash infrastructure/k8s/scripts/construir-imagenes.sh
wsl -d Ubuntu-24.04 -u root bash infrastructure/k8s/scripts/importar-imagenes-k3s.sh

# 4. Desplegar
bash infrastructure/k8s/scripts/desplegar.sh local      # → http://localhost
```

Producción con dominio y HTTPS:
[infrastructure/k8s/README.md](infrastructure/k8s/README.md#5-desplegar).

### Docker Compose (desarrollo)

```bash
cp .env.example .env
bash infrastructure/docker/generar-llaves.sh
docker compose up -d --build
```

Web en <http://127.0.0.1:10001>, gateway en <http://127.0.0.1:10000>. Para publicar también los
puertos de las bases: `docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d`.

### Cuentas de demostración

Con los datos migrados de la Fase 2:

| Rol | Correo | Contraseña |
|---|---|---|
| ADMIN | `admin@swmatrc.org` | `Admin.2026` |
| OPERATOR | `operador@swmatrc.org` | `Operador.2026` |
| VIEWER | `consulta@swmatrc.org` | `Consulta.2026` |

Además, con las credenciales de Google o GitHub configuradas, aparecen los botones
**Continuar con Google** y **Continuar con GitHub** en la pantalla de acceso.

---

## Pruebas

```bash
cd services/auth-service && npm test                         # JWT y proveedores OAuth/OIDC
cd services && dotnet test SWMatrc.Servicios.slnx            # gateway, Community, Sensor
docker build --target pruebas services/report-service        # Report con PDF real
python tools/pruebas/e2e.py                                  # todo el sistema por el gateway
python tools/pruebas/contrato_frontend.py                    # llamadas del frontend por nginx
```

La prueba de integración del flujo OAuth necesita un PostgreSQL vacío
(ver [test/oauth.e2e-spec.ts](services/auth-service/test/oauth.e2e-spec.ts)). El CI corre todo
lo anterior, valida los manifiestos de Kubernetes y, en `main`, publica las imágenes en GHCR.

---

## Tecnologías

| Área | Tecnologías |
|---|---|
| Lenguajes | C# (.NET 10), TypeScript (NestJS 12, Angular 22), Java 25 (Spring Boot 4), Python 3.14 (FastAPI) |
| Bases de datos | PostgreSQL 18, SQL Server 2022, MongoDB 8, Redis 7 (backplane de SignalR) |
| Identidad | OAuth 2.0 + PKCE, OpenID Connect, JWT RS256 + JWKS, Google, GitHub |
| Infraestructura | Docker, K3s (Kubernetes), Traefik (Ingress), cert-manager + Let's Encrypt, Kustomize |
| Tiempo real y reportes | SignalR (WebSocket), Jinja2 + WeasyPrint (PDF), GridFS |
