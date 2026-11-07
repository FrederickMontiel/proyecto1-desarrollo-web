# Fase 4 — Autenticación externa, Kubernetes/K3s y arquitectura distribuida

Evolución final del sistema de monitoreo y alerta temprana. Se conserva la arquitectura de
microservicios de la [Fase 3](FASE-3.md) y se le agregan:

- **Autenticación externa** con OAuth 2.0 y OpenID Connect (Google y GitHub), centralizada en
  el Auth Service y extensible a cualquier otro proveedor OIDC.
- **Orquestación con Kubernetes/K3s**: Pods, Deployments, Services, Ingress, ConfigMaps,
  Secrets, Persistent Volumes, health checks y escalabilidad horizontal.

**Leyenda:** ✅ implementado y probado · 🟡 implementado, falta un paso del usuario (credenciales, dominio)

---

## 1. Criterios de finalización (sección 17)

| Criterio | Dónde | Estado |
|---|---|---|
| Autenticación con Google y GitHub mediante Auth Service | [`services/auth-service/src/oauth/`](../services/auth-service/src/oauth/) | ✅ código y pruebas · 🟡 faltan los Client ID/Secret reales |
| Generación y validación de JWT en los microservicios | Auth emite; gateway, .NET, Java y Python validan (§5) | ✅ |
| Despliegue en Kubernetes/K3s (Pods, Deployments, Services, Ingress, ConfigMaps, Secrets) | [`infrastructure/k8s/`](../infrastructure/k8s/) | ✅ manifiestos validados · 🟡 aplicarlos en el clúster |
| Almacenamiento persistente en las bases de datos | StatefulSets con `volumeClaimTemplates` (§7.3) | ✅ |
| Health checks activos | `/health/live` y `/health/ready` en los 7 procesos; sondas de arranque, vida y disponibilidad | ✅ |
| Accesible por DNS con HTTPS | Ingress + cert-manager + Let's Encrypt ([overlay de producción](../infrastructure/k8s/overlays/produccion/)) | 🟡 falta el dominio real |
| Dashboard y reportería en arquitectura heterogénea (3+ lenguajes, 3+ BD) | C#, TypeScript, Java y Python; PostgreSQL, SQL Server y MongoDB | ✅ |

---

## 2. Arquitectura final

```text
                              INTERNET
                                 │
                          DNS / HTTPS (443)
                                 │
                 ┌───────────────▼────────────────┐
                 │ Ingress (Traefik de K3s)        │  TLS de Let's Encrypt (cert-manager)
                 │  /api, /hubs → api-gateway      │
                 │  /           → web (Angular)    │
                 └───────┬─────────────────┬───────┘
                         │                 │
                ┌────────▼───────┐   ┌─────▼─────┐
                │  API Gateway   │   │    web    │  nginx + Angular (2 réplicas)
                │ .NET 10 + YARP │   └───────────┘
                │  (2–5 réplicas)│  valida JWT: firma, iss, aud, exp y rol
                └───────┬────────┘
     ┌──────────────┬───┴──────────┬───────────────┬──────────────┬──────────────┐
┌────▼─────┐  ┌─────▼─────┐  ┌─────▼──────┐  ┌─────▼─────┐  ┌─────▼─────┐  ┌─────▼─────┐
│  Auth    │  │ Community │  │  Sensor    │  │  Alert    │  │  Report   │  │  Audit    │
│ NestJS   │  │ .NET 10   │  │ .NET 10    │  │ Java 25   │  │ Python    │  │ Python    │
│ OAuth2 / │  │           │  │ + SignalR  │  │ Spring    │  │ FastAPI + │  │ FastAPI   │
│ OIDC/JWT │  │           │  │ + simulador│  │ Boot 4    │  │ WeasyPrint│  │           │
└──┬───┬───┘  └─────┬─────┘  └──┬─────┬───┘  └─────┬─────┘  └─────┬─────┘  └─────┬─────┘
   │   │            │           │     │            │              │              │
   │ Google/GitHub  │           │   Redis          │              │              │
   │ (OIDC/OAuth2)  │           │ (backplane)      │              │              │
┌──▼────────────────▼───┐  ┌────▼──────┐  ┌────────▼───┐   ┌──────▼──────────────▼──┐
│ PostgreSQL 18          │  │ SQL Server│  │ PostgreSQL │   │ MongoDB 8              │
│ auth_db · community_db │  │ sensor_db │  │ alert_db   │   │ report_db · audit_db   │
└────────────────────────┘  └───────────┘  └────────────┘   └────────────────────────┘
         (un StatefulSet por motor, cada uno con su PersistentVolumeClaim)
```

Todo lo anterior corre dentro del namespace `swmatrc` del clúster K3s.

| Componente | Lenguaje / tecnología | Base | Réplicas |
|---|---|---|---|
| api-gateway | C# · ASP.NET Core 10 + YARP | — | 2 (HPA hasta 5) |
| auth-service | TypeScript · NestJS 12 | PostgreSQL `auth_db` | 2 (HPA hasta 4) |
| community-service | C# · .NET 10 + EF Core | PostgreSQL `community_db` | 2 (HPA hasta 4) |
| sensor-service | C# · .NET 10 + SignalR | SQL Server `sensor_db` | 2 (HPA hasta 4) |
| sensor-simulador | misma imagen que sensor-service | SQL Server `sensor_db` | 1 (fija) |
| alert-service | Java 25 · Spring Boot 4 | PostgreSQL `alert_db` | 2 (HPA hasta 4) |
| report-service | Python 3.14 · FastAPI + Jinja2 + WeasyPrint | MongoDB `report_db` (+GridFS) | 2 (HPA hasta 4) |
| audit-service | Python 3.14 · FastAPI | MongoDB `audit_db` | 2 (HPA hasta 3) |
| web | Angular 22 + nginx | — | 2 (HPA hasta 3) |
| redis | Redis 7.4 | — (en memoria) | 1 |

---

## 3. Autenticación externa (secciones 4, 5 y 7)

### 3.1 Flujo

```text
 Navegador            Angular            Auth Service               Google / GitHub
     │  clic "Continuar con Google"                │                          │
     │──────────────────────────────────────────▶ GET /api/auth/login/google │
     │                                  guarda state + PKCE + nonce (PostgreSQL)
     │◀─────────── 302 + cookie HttpOnly "state" ──│                          │
     │──────────────────────────────────────────────────────────────────────▶ │ login y consentimiento
     │◀───────────────────────────────── 302 /api/auth/callback/google?code&state
     │──────────────────────────────────────────▶ │                          │
     │                     1. state == cookie y existe (un solo uso)         │
     │                     2. canjea code + code_verifier ─────────────────▶ │
     │                                             │◀──────── id_token ───────│
     │                     3. valida id_token: firma (JWKS del proveedor),   │
     │                        iss, aud = client_id, exp, nonce               │
     │                     4. crea o vincula el usuario                      │
     │                     5. código de un solo uso (60 s)                   │
     │◀──── 302 /login/callback?codigo=… ──────────│                          │
     │──▶ Angular: POST /api/auth/exchange {codigo} ▶ emite el JWT del sistema
     │◀── { token, expira, usuario, proveedor } ───│
     │  Angular → API Gateway → microservicios con  Authorization: Bearer <JWT>
```

Los microservicios **nunca** hablan con Google ni con GitHub ni guardan sus credenciales: solo
validan el JWT que emite el Auth Service (sección 7 del documento).

### 3.2 Requisitos

| Requisito | Cómo se cumple | Dónde |
|---|---|---|
| RF-OAUTH-01 OAuth 2.0 | Flujo *authorization code* con PKCE (RFC 7636, `S256`) y `state` | [oauth.service.ts](../services/auth-service/src/oauth/oauth.service.ts) |
| RF-OAUTH-02 OpenID Connect | Descubrimiento (`/.well-known/openid-configuration`), validación del `id_token` con el JWKS del proveedor, `nonce`, claims `sub`, `email`, `email_verified`, `name`, `picture` | [`ProveedorOidc`](../services/auth-service/src/oauth/proveedores.ts) |
| RF-OAUTH-03 Google | Proveedor OIDC con emisor `https://accounts.google.com`, alcances `openid email profile` | [configuracion.ts](../services/auth-service/src/configuracion.ts) |
| RF-OAUTH-04 GitHub | OAuth 2.0 + API REST (`/user`, `/user/emails`): GitHub no emite `id_token` para OAuth Apps | [`ProveedorGithub`](../services/auth-service/src/oauth/proveedores.ts) |
| RF-OAUTH-05 Extensibilidad | Cualquier proveedor OIDC se agrega **solo con variables de entorno** (abajo). Uno que no sea OIDC (Facebook) implementa la interfaz `ProveedorIdentidad` | [proveedores.ts](../services/auth-service/src/oauth/proveedores.ts) |

Agregar Microsoft (o Keycloak, Auth0, GitLab…) sin tocar código:

```env
OAUTH_OIDC_PROVEEDORES=microsoft
OAUTH_MICROSOFT_NOMBRE=Microsoft
OAUTH_MICROSOFT_EMISOR=https://login.microsoftonline.com/<tenant-id>/v2.0
OAUTH_MICROSOFT_CLIENT_ID=...
OAUTH_MICROSOFT_CLIENT_SECRET=...
```

El botón "Continuar con Microsoft" aparece solo: la pantalla de acceso dibuja lo que devuelve
`GET /api/auth/providers`.

### 3.3 Creación y vinculación de usuarios

1. Si la identidad externa (`proveedor` + `sub`) ya está vinculada → entra ese usuario.
2. Si no, y hay un usuario con el mismo **correo verificado** por el proveedor → se vincula. Un
   usuario migrado de las fases anteriores entra con Google **conservando su rol**.
3. Si no existe → se crea con rol **VIEWER** y sin contraseña local. Un ADMIN puede ampliarle el
   rol desde la pantalla de usuarios. Con `OAUTH_REGISTRO_ABIERTO=false` solo entran correos
   ya registrados.

Un usuario puede tener varias identidades (Google y GitHub) en `identidades_externas`. Las cuentas
deshabilitadas no entran por ninguna vía. Todo queda en la auditoría: `InicioSesionExterno`,
`UsuarioCreado` (con el proveedor) e `IdentidadExternaVinculada`.

### 3.4 Endpoints

| Método | Ruta | Acceso | Descripción |
|---|---|---|---|
| GET | `/api/auth/providers` | público | Proveedores habilitados `[{id, nombre, url}]` |
| GET | `/api/auth/login/{proveedor}` | público | Redirige al proveedor (302) |
| GET | `/api/auth/callback/{proveedor}` | público | Vuelta del proveedor; redirige a `/login/callback` |
| POST | `/api/auth/exchange` | público | Canjea el código de un solo uso por el JWT |
| POST | `/api/auth/login` | público | Login local (correo y contraseña), se conserva |
| POST | `/api/auth/logout` | token | Revoca la sesión (el `jti`) |
| GET | `/.well-known/jwks.json` | interno | Llave pública para validar los JWT |

Las URL de callback que se registran en los proveedores (sección 9):

```text
https://sistema-climatico.example/api/auth/callback/google
https://sistema-climatico.example/api/auth/callback/github
```

En local (K3s en WSL) son `http://localhost/api/auth/callback/...`. Cómo obtener las credenciales:
[infrastructure/k8s/README.md](../infrastructure/k8s/README.md#4-credenciales-de-google-y-github).

### 3.5 Medidas de seguridad del flujo

| Amenaza | Defensa |
|---|---|
| CSRF en el callback (*login CSRF*) | `state` aleatorio guardado en PostgreSQL **y** en una cookie `HttpOnly; Secure; SameSite=Lax` limitada a `/api/auth/callback`; deben coincidir |
| Robo del `code` | PKCE `S256`: sin el `code_verifier` (que nunca sale del servidor) el código no sirve |
| Reutilización de un `id_token` | `nonce` por solicitud, verificado contra el guardado |
| `id_token` falsificado o de otra app | Firma con el JWKS del proveedor, `iss` y `aud = client_id` |
| Token en la URL (historial, logs del proxy) | La URL solo lleva un código de 60 s y un solo uso; el JWT viaja en el cuerpo de un POST. Se guarda solo el SHA-256 del código |
| Reintentos del mismo `state` o código | Se consumen con `DELETE … RETURNING`: atómico aun con varias réplicas |
| Fuerza bruta | El gateway limita `/api/auth/login*`, `/callback` y `/exchange` a 10 por minuto por IP |
| Secretos en el repositorio | Client ID/Secret solo en `.env` (no versionado) → Secret de Kubernetes montado como archivo |

---

## 4. JWT (sección 6)

Ejemplo real de un token emitido tras entrar con Google:

```json
{
  "sub": "14",
  "email": "usuario@example.com",
  "name": "Usuario",
  "roles": ["VIEWER"],
  "permissions": ["alertas:leer", "comunidades:leer", "sensores:leer"],
  "provider": "google",
  "iss": "swmatrc-auth",
  "aud": "swmatrc-web",
  "iat": 1794000000,
  "exp": 1794028800,
  "jti": "5b0d1c3e-…"
}
```

- Firma **RS256** con la llave privada de Auth (Secret `swmatrc-jwt`, montado como archivo). La
  cabecera lleva `kid` (huella RFC 7638) y la pública se publica en `/.well-known/jwks.json`.
- `iss` y `aud` son configurables (`JWT_EMISOR`, `JWT_AUDIENCIA` en el ConfigMap).
- Los tokens de servicio a servicio usan otra audiencia (`swmatrc-internal`) y el rol `SERVICE`:
  un token de usuario no sirve en `/internal` y uno de servicio no sirve en `/api`.

### 4.1 Validación en cada microservicio

"Los microservicios deberán validar: Firma, Issuer, Audience, Expiración, Roles y Permisos."

| Servicio | Librería | Firma | iss | aud | exp | Roles | Permisos |
|---|---|---|---|---|---|---|---|
| API Gateway (C#) | JwtBearer | ✅ JWKS | ✅ | ✅ | ✅ | ✅ rol del sistema; ADMIN en rutas administrativas | — (los servicios) |
| Auth (TypeScript) | jose | ✅ | ✅ | ✅ | ✅ | ✅ `@RequiereRoles` | ✅ + sesión no revocada |
| Community, Sensor (C#) | JwtBearer + políticas | ✅ JWKS | ✅ | ✅ | ✅ | ✅ `RequireRole` | ✅ una política por permiso |
| Alert (Java) | Spring Security OAuth2 Resource Server | ✅ JWKS | ✅ | ✅ | ✅ | ✅ `ROLE_*` | ✅ `PERM_*` en la cadena y `@PreAuthorize` |
| Report, Audit (Python) | PyJWT | ✅ JWKS | ✅ | ✅ | ✅ | ✅ | ✅ dependencia `requiere_permiso` |

Un token vencido se rechaza en todos con 401 (RNF-OAUTH-04), con 60 s de tolerancia de reloj.

---

## 5. Roles (sección 15)

| Rol | En pantalla | Permisos |
|---|---|---|
| **ADMIN** | Administrador | Todos: usuarios, comunidades, sensores, reglas y umbrales, alertas, reportes (incluida la descarga), auditoría y mantenimiento |
| **OPERATOR** | Operador | Lectura + atender alertas, operar sensores, corregir lecturas, generar reportes |
| **VIEWER** | Consulta | Lectura del dashboard, alertas, sensores e historial |

Los códigos viajan en el claim `roles`. Los roles de la Fase 3 (`Administrador`, `Operador`,
`Consulta`) se renombraron con una migración versionada
([migraciones.ts](../services/auth-service/src/base-datos/migraciones.ts)): cada usuario conserva
su rol.

**RNF-OAUTH-03 — rutas administrativas protegidas por roles.** Dos barreras:

1. **Gateway**: `/api/auth/users`, `/api/auth/roles`, `/api/audit` y `/api/sensors/system` exigen
   el rol ADMIN antes de llegar al servicio.
2. **Cada servicio**: los permisos administrativos (`comunidades:editar`, `sensores:editar`,
   `reglas:editar`, `reportes:descargar`, `auditoria:leer`, `usuarios:administrar`,
   `sistema:administrar`) exigen además el rol ADMIN. Un token con el permiso pero sin el rol
   recibe 403 (hay pruebas de esto en Audit y Report).

---

## 6. Seguridad (sección 8)

| Requisito | Cómo |
|---|---|
| RNF-OAUTH-01 Secretos fuera del código | `.env` y `secrets/` están en `.gitignore`; `.env.example` solo tiene marcadores |
| RNF-OAUTH-02 Secretos en Kubernetes Secrets | `swmatrc-db`, `swmatrc-jwt`, `swmatrc-oauth`, `swmatrc-servicios`, `swmatrc-admin`, creados por [crear-secretos.sh](../infrastructure/k8s/scripts/crear-secretos.sh). La llave JWT y los Client Secrets se montan como **archivos** de solo lectura |
| RNF-OAUTH-03 Rutas administrativas por rol | §5 |
| RNF-OAUTH-04 JWT expirados | `exp` obligatorio en todos los validadores |
| RNF-OAUTH-05 Identidad en APIs internas | `/internal/*` exige token de servicio (audiencia interna + rol `SERVICE`); el gateway no enruta `/internal` |

Además, en el clúster:

- **NetworkPolicies** ([politicas-red.yaml](../infrastructure/k8s/base/politicas-red.yaml)): todo
  el tráfico entrante está denegado salvo lo necesario. Por ejemplo, SQL Server solo acepta
  conexiones de Sensor.
- **Pods sin privilegios**: `runAsNonRoot`, `allowPrivilegeEscalation: false`, todas las
  capacidades retiradas, `seccompProfile: RuntimeDefault` y sistema de archivos de solo lectura
  donde la aplicación lo permite.
- **Pod Security Standards** `baseline` obligatorio en el namespace.

---

## 7. Kubernetes / K3s (secciones 10–14)

Manifiestos con **Kustomize**: una base común y dos overlays.

```text
infrastructure/k8s/
├── base/
│   ├── namespace.yaml          Namespace swmatrc (Pod Security Standards)
│   ├── configmap.yaml          ConfigMap: URLs internas, entorno, banderas
│   ├── bases-de-datos/         StatefulSet + PVC + Service: postgres, sqlserver (+Job init), mongo; redis
│   ├── servicios/              Deployment + Service por microservicio, gateway y web
│   ├── escalado.yaml           HorizontalPodAutoscaler + PodDisruptionBudget
│   ├── ingress.yaml            Ingress (Traefik)
│   └── politicas-red.yaml      NetworkPolicies
├── overlays/
│   ├── local/                  K3s en WSL: http://localhost, imágenes importadas
│   └── produccion/             dominio + TLS (cert-manager/Let's Encrypt) + imágenes de GHCR
├── k3s/traefik-config.yaml     WebSocket sin límite de lectura en Traefik
└── scripts/                    instalar, construir/importar imágenes, secretos, desplegar, trasladar datos
```

### 7.1 Pods, Deployments y Services (secciones 11 y 12)

- Cada microservicio es un **Deployment** con 2 réplicas, actualización gradual
  (`maxUnavailable: 0`) y un **Service** `ClusterIP` con el mismo nombre que tenía en Compose
  (`auth-service:3000`, `community-service:8080`…). Por eso la configuración de YARP no cambió.
- **Sensor** se separa en dos Deployments de la misma imagen: `sensor-service` (API + hub SignalR,
  escalable) y `sensor-simulador` (1 réplica, `strategy: Recreate`), porque el simulador guarda
  los episodios climáticos en memoria. Con varias réplicas del hub, **Redis** hace de *backplane*:
  un evento emitido en un pod llega a los navegadores conectados a cualquier otro. El cliente usa
  WebSocket sin negociación, así que no hacen falta sesiones pegajosas.

### 7.2 Ingress (sección 12)

Único punto de entrada: `/api` y `/hubs` → `api-gateway`, el resto → `web`. En producción,
cert-manager obtiene y renueva el certificado de Let's Encrypt, y un Middleware de Traefik
redirige HTTP → HTTPS. Traefik se configura sin límite de lectura para que el WebSocket de
alertas no se corte a los 60 s.

### 7.3 ConfigMaps, Secrets y persistencia (secciones 13 y 14)

- **ConfigMap** `swmatrc-config`: URLs internas, URL pública, emisor y audiencia del JWT, zona
  horaria, intervalo del simulador y la bandera `OAUTH_REGISTRO_ABIERTO`.
- **Secrets**: ver §6. No hay ningún Secret en el repositorio.
- **Persistencia**: PostgreSQL, SQL Server y MongoDB son **StatefulSets** con
  `volumeClaimTemplates` (5 Gi, 10 Gi y 5 Gi). En K3s los atiende `local-path-provisioner`. Borrar
  un pod no borra los datos: el nuevo pod monta el mismo volumen.
- Los scripts de inicialización de `database/` llegan como ConfigMaps. Para SQL Server, un **Job**
  idempotente crea `sensor_db` y su login.

### 7.4 Health checks (sección 14)

| Sonda | Ruta | Si falla |
|---|---|---|
| `startupProbe` | `/health/live` | Da tiempo al arranque (hasta 3 min para la JVM) antes de activar las demás |
| `livenessProbe` | `/health/live` | Kubernetes reinicia el contenedor |
| `readinessProbe` | `/health/ready` (comprueba su base de datos) | El Service deja de mandarle tráfico, sin reiniciarlo |

Las bases usan `pg_isready`, `sqlcmd SELECT 1` y `mongosh ping`.

### 7.5 Escalabilidad horizontal

HPA por CPU (70 %) para el gateway, los seis microservicios y la web (K3s trae metrics-server).
Los PodDisruptionBudget mantienen al menos una réplica durante un drenado o una actualización.
Demostración:

```bash
kubectl -n swmatrc scale deployment/alert-service --replicas=4
kubectl -n swmatrc get hpa
```

---

## 8. Sensores reales (IoT con Raspberry Pi)

Además de los sensores simulados, el sistema recibe lecturas de **sensores físicos**: una
Raspberry Pi 4 con un DHT11 (temperatura y humedad) y un SW-420 (vibración) en la comunidad
*Estación IoT UMG*. Detalles y cableado: [iot/raspberry/README.md](../iot/raspberry/README.md).

| Pieza | Dónde |
|---|---|
| Cliente en la Raspberry (lee GPIO y envía cada 5 s) | [iot/raspberry/sensores_iot.py](../iot/raspberry/sensores_iot.py) |
| Endpoint `POST /api/sensors/iot/readings` (cabecera `X-Api-Key`) | [Sensor Service · Iot/](../services/sensor-service/src/SWMatrc.Sensores/Iot/) |
| Ruta anónima en el gateway (el dispositivo no usa JWT; Sensor valida su llave) | `sensors-iot` en [appsettings.json](../services/api-gateway/SWMatrc.Gateway/appsettings.json) |
| Llave del dispositivo | Secret `swmatrc-iot` (solo lo reciben las réplicas de la API de Sensor) |

Cómo encaja: la API guarda el valor recibido en el sensor (`ValorRecibido`, `RecibidoEn`) y el
ciclo de adquisición (pod `sensor-simulador`) lo convierte en lectura en su siguiente vuelta.
Para los sensores físicos no se llama al simulador: el valor real pasa por el mismo camino que
uno simulado (evaluación en Alert, nivel, historial y SignalR). Si el dispositivo calla más de
30 s, el sensor queda **sin señal**. Cada dispositivo solo puede reportar sus sensores, y los
valores fuera del rango físico se rechazan. Pruebas: 8 en `IotTests.cs`.

Verificado en el clúster K3s con lecturas reales del DHT11 (25.6 °C, 59 %): 202 aceptadas,
evaluadas por Alert (nivel Verde), guardadas en el historial; sin llave o con llave falsa → 401;
sensor ajeno → rechazado.

---

## 9. Estructura del repositorio (sección 16)

```text
├── README.md
├── docs/                      FASE-1.md … FASE-4.md
├── frontend/                  Angular
├── services/
│   ├── api-gateway/  auth-service/  community-service/  sensor-service/
│   ├── alert-service/  report-service/  audit-service/
│   └── dotnet-comun/          librería compartida de los servicios .NET
├── infrastructure/
│   ├── docker/                generación de llaves, respaldos y cortafuegos
│   ├── nginx/                 proxy inverso de la VPS (alternativa a K3s)
│   └── k8s/                   Kubernetes / K3s
├── database/                  postgres/  sqlserver/  mongodb/  (scripts de inicialización)
├── iot/raspberry/             cliente de la estación de sensores reales
├── tools/                     migración de datos y pruebas de punta a punta
└── .github/workflows/         CI: compilación, pruebas, validación de manifiestos e imágenes
```

`docker-compose.yml` se queda en la raíz como entorno de desarrollo (`docker compose up`).

---

## 10. Pruebas

| Conjunto | Cantidad | Qué cubre |
|---|---|---|
| Auth: unitarias (Vitest) | 18 | JWT RS256 y JWKS; proveedor OIDC (PKCE, nonce, firma ajena, audiencia, correo sin verificar, reintento del descubrimiento); proveedor GitHub |
| Auth: integración OAuth (Vitest + PostgreSQL) | 11 | Flujo completo: alta VIEWER, segundo ingreso sin duplicar, vinculación conservando el rol, CSRF, `state` reutilizado, código de un solo uso, cancelación, cuenta deshabilitada, VIEWER sin acceso administrativo |
| .NET (xUnit) | 41 | Community y Sensor, incluidas 8 de sensores físicos (IoT) |
| Alert (JUnit) | 42 | Motor de riesgo y conciliación de alertas |
| Audit (pytest) | 19 | Incluye rol ADMIN obligatorio, token sin rol y rol SERVICE en `/internal` |
| Report (pytest) | 26 | Incluye descarga con permiso pero sin rol ADMIN → 403; PDF real en Docker |
| Punta a punta ([e2e.py](../tools/pruebas/e2e.py)) | 57 | Todo el sistema por el gateway, incluidos los claims `roles`/`provider` y las rutas OAuth |
| Contrato del frontend | 38 | Cada llamada de `api.ts` a través de nginx |
| CI | — | Además valida los manifiestos con kubeconform y publica las imágenes en GHCR |

---

## 11. Limitaciones conocidas

- **Bases de datos con una réplica.** Los StatefulSets tienen persistencia, pero no alta
  disponibilidad (no hay réplica de PostgreSQL ni conjunto de réplicas de MongoDB). En una VPS de
  un nodo no aportaría: si el nodo cae, cae todo.
- **Reinicio del sistema y simulador.** `POST /api/sensors/system/reset` restablece la base, pero
  los episodios climáticos en memoria del pod `sensor-simulador` siguen hasta que terminan solos.
- **Recursos.** Las *requests* suman unos 5.5 GiB de memoria (SQL Server pide 2 GiB). La VPS
  necesita al menos 8 GB, o bajar las réplicas a 1 (`kubectl scale`).
- **Auditoría "dispara y olvida".** Si Audit está caído, los eventos de ese lapso se pierden
  (heredado de la Fase 3).
