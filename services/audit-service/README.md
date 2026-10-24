# Audit Service

Bitácora centralizada de SWMATRC, hecha con FastAPI y MongoDB. Todos los microservicios le reportan las acciones relevantes: inicios de sesión, cambios de usuarios, sensores, alertas y reportes. Los administradores consultan esa bitácora desde el frontend.

## Endpoints

| Método | Ruta | Acceso |
|---|---|---|
| GET | `/api/audit/events` | permiso `auditoria:leer` |
| GET | `/api/audit/catalogs` | permiso `auditoria:leer` |
| POST | `/internal/events` | token de servicio |
| POST | `/internal/events/batch` | token de servicio (hasta 5000 eventos; lo usa la migración) |
| GET | `/internal/docs` | documentación OpenAPI, solo desde la red interna |
| GET | `/health/live`, `/health/ready` | sondas |

`GET /api/audit/events` admite los filtros `usuario` (correo o parte de él), `accion`, `entidad`, `servicio`, `desde`, `hasta`, `pagina` y `tamano` (máximo 200).

Respuesta paginada:

```json
{ "elementos": [], "pagina": 1, "tamanoPagina": 30, "totalElementos": 0, "totalPaginas": 0 }
```

## Evento

```json
{
  "servicio": "auth-service",
  "usuarioId": 1,
  "usuarioEmail": "admin@swmatrc.local",
  "accion": "InicioSesion",
  "entidad": "Usuario",
  "entidadId": "1",
  "detalle": { "rol": "Administrador" },
  "direccionIp": "10.0.0.5",
  "correlacion": "4795d8cd4a1f4b01bee20d807cacc13a",
  "fechaHora": "2026-10-22T22:05:38Z"
}
```

## Configuración

| Variable | Descripción |
|---|---|
| `MONGO_HOST`, `MONGO_PUERTO`, `MONGO_BASE`, `MONGO_USUARIO` | Conexión a `audit_db`. |
| `MONGO_PASSWORD` o `MONGO_PASSWORD_FILE` | Contraseña del usuario `audit_svc`. |
| `AUTH_URL_METADATOS` | Documento `openid-configuration` del Auth Service. |
| `JWT_EMISOR`, `JWT_AUDIENCIA`, `JWT_AUDIENCIA_INTERNA` | Valores esperados de `iss` y `aud`. |

## Desarrollo

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-dev.txt   # en Linux: .venv/bin/python
.venv/Scripts/python -m pytest
```
