# Fase 3 — Reingeniería a microservicios

El monolito de la [Fase 2](FASE-2.md) se dividió en seis microservicios independientes, cada uno
con su propia base de datos, detrás de un API Gateway. Se usaron cuatro lenguajes y tres motores
de base de datos. La [Fase 4](FASE-4.md) conserva esta arquitectura y la lleva a Kubernetes.

---

## 1. Servicios

| Servicio | Stack | Base de datos | Puerto | Rutas públicas (gateway) |
|---|---|---|---|---|
| api-gateway | C# · ASP.NET Core 10 + YARP | — | 8080 | punto de entrada |
| auth-service | TypeScript · NestJS + TypeORM | PostgreSQL `auth_db` | 3000 | `/api/auth` |
| community-service | C# · .NET 10 + EF Core (Npgsql) | PostgreSQL `community_db` | 8080 | `/api/communities` |
| sensor-service | C# · .NET 10 + EF Core + SignalR | SQL Server `sensor_db` | 8080 | `/api/sensors`, `/hubs/monitoring` |
| alert-service | Java 25 · Spring Boot 4 + JPA + Flyway | PostgreSQL `alert_db` | 8080 | `/api/alerts` |
| report-service | Python 3.14 · FastAPI + Jinja2 + WeasyPrint | MongoDB `report_db` (+GridFS) | 8000 | `/api/reports` |
| audit-service | Python 3.14 · FastAPI + PyMongo | MongoDB `audit_db` | 8000 | `/api/audit` |
| web | Angular 22 + nginx | — | 80 | `/` |

Cada servicio tiene su propio usuario de base de datos y no puede leer las bases de los demás.

## 2. Responsabilidades

- **Auth** — usuarios, roles, permisos, login/logout con sesiones revocables y emisión de JWT
  RS256. Publica `/.well-known/jwks.json` y `/.well-known/openid-configuration`, así que nadie
  más necesita un secreto compartido. Emite además tokens de servicio a servicio (audiencia
  `swmatrc-internal`).
- **Community** — comunidades rurales, su GeoJSON y la cantidad de sensores (consultada a Sensor).
- **Sensor** — sensores, lecturas, simulador climático y canal en tiempo real (SignalR). En cada
  ciclo manda las lecturas a Alert para evaluarlas y difunde los cambios.
- **Alert** — motor de riesgo (inundación, tormenta, helada, sequía, incendio forestal), reglas,
  umbrales por sensor, alertas con su ciclo de vida e historial de eventos.
- **Report** — los 8 reportes PDF (RF-REP-01…08), generación asíncrona, historial y dashboard
  estadístico. Obtiene los datos de los demás servicios por API interna.
- **Audit** — bitácora centralizada; los demás servicios le mandan sus eventos.

## 3. Comunicación

```text
Navegador ──▶ nginx (web) ──▶ API Gateway ──▶ /api/* de cada servicio     (token de usuario)

Sensor ──▶ Community  GET  /internal/communities          (comunidades activas)
Sensor ──▶ Alert      POST /internal/evaluate             (niveles y alertas del ciclo)
Alert  ──▶ Sensor     POST /internal/notify               (alerta atendida/cerrada → SignalR)
Community ──▶ Sensor  GET  /internal/sensors/count-by-community
Report ──▶ Community, Sensor, Alert  /internal/*           (datos de los reportes)
Todos  ──▶ Audit      POST /internal/events[/batch]       (bitácora)
Todos  ──▶ Auth       POST /internal/token                (token de servicio, 15 min)
```

Las rutas `/internal` exigen un token de servicio y el gateway no las enruta: solo se alcanzan
dentro de la red interna.

## 4. Decisiones

| Tema | Decisión |
|---|---|
| JWT | RS256 + JWKS publicado por Auth: el gateway y cada servicio validan firma, emisor, audiencia, vigencia y permisos sin compartir secretos |
| Tiempo real | SignalR se queda en Sensor; Alert devuelve las alertas nuevas en la respuesta de la evaluación |
| Datos | Se migraron los de la Fase 2 (`tools/migracion/migrar.py`) conservando ids y hashes BCrypt |
| Rutas | HTTP en inglés (`/api/communities`); contenido y código en español |
| Resiliencia | Clientes HTTP con reintento, tiempo de espera y cortocircuito; si un servicio cae, los demás siguen (p. ej., sin Alert las lecturas se guardan sin nivel) |
| Errores | ProblemDetails en español en todos los servicios y en el gateway |
| Contenedores | Imágenes multietapa, usuario sin privilegios, sondas `/health/live` y `/health/ready`, apagado ordenado con SIGTERM |

## 5. Pruebas

Unitarias por servicio (Vitest, xUnit, JUnit, pytest), `tools/pruebas/e2e.py` (todo el sistema
a través del gateway, incluido SignalR por WebSocket) y `tools/pruebas/contrato_frontend.py`
(cada llamada del frontend a través de nginx).

## 6. Ejecución con Docker Compose

```bash
cp .env.example .env                       # y cambiar las contraseñas
bash infrastructure/docker/generar-llaves.sh
docker compose up -d --build
# Web: http://127.0.0.1:10001   Gateway: http://127.0.0.1:10000
```

Migración de los datos de la Fase 2:

```bash
docker compose stop auth-service community-service sensor-service alert-service report-service audit-service
docker compose --profile migracion run --rm migracion
docker compose up -d
```
