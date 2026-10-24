# Auth Service

Microservicio de autenticación de SWMATRC (NestJS + PostgreSQL). Administra usuarios, roles y permisos, y emite los JWT con los que el frontend entra a los demás servicios a través del gateway.

## Responsabilidades

- Login y logout. El logout revoca la sesión.
- Usuarios, roles (`Consulta`, `Operador`, `Administrador`) y permisos.
- Emisión de JWT **RS256**. La llave pública se publica en `/.well-known/jwks.json`.
- Tokens de servicio para las llamadas entre microservicios.
- Tabla `identidades_externas`, lista para vincular cuentas de Google, GitHub u otro proveedor OIDC en la Fase 4.

## Endpoints

| Método | Ruta | Acceso |
|---|---|---|
| POST | `/api/auth/login` | público |
| POST | `/api/auth/logout` | autenticado |
| GET | `/api/auth/me` | autenticado |
| POST | `/api/auth/me/password` | autenticado |
| GET / POST | `/api/auth/users` | `usuarios:administrar` |
| GET / PUT | `/api/auth/users/{id}` | `usuarios:administrar` |
| PATCH | `/api/auth/users/{id}/status` | `usuarios:administrar` |
| POST | `/api/auth/users/{id}/password` | `usuarios:administrar` |
| GET | `/api/auth/roles` | `usuarios:administrar` |
| GET | `/.well-known/jwks.json` | red interna |
| GET | `/.well-known/openid-configuration` | red interna |
| POST | `/internal/token` | red interna (credenciales de servicio) |
| GET | `/internal/sessions/{jti}` | token de servicio |
| GET | `/health/live`, `/health/ready` | sondas |

## Configuración

| Variable | Descripción |
|---|---|
| `DB_HOST`, `DB_PORT`, `DB_NOMBRE`, `DB_USUARIO`, `DB_PASSWORD` | Conexión a `auth_db`. |
| `JWT_LLAVE_PRIVADA_FILE` | Ruta a la llave privada RSA en PEM. Se monta como secreto. |
| `JWT_EMISOR`, `JWT_AUDIENCIA`, `JWT_AUDIENCIA_INTERNA` | Valores de `iss` y `aud`. |
| `JWT_MINUTOS_VIGENCIA` | Vigencia del token de usuario. |
| `CLIENTES_SERVICIO` | `id:secreto,id:secreto` de los servicios que piden token interno. |
| `URL_INTERNA`, `URL_AUDITORIA` | Direcciones dentro de la red de Docker. |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `ADMIN_NOMBRE` | Administrador inicial. Solo se crea si no existe ningún usuario. |

Cualquier variable sensible también se puede leer de un archivo agregando `_FILE` al nombre (por ejemplo `DB_PASSWORD_FILE`). Así es como Docker y Kubernetes montan los Secrets.

## Desarrollo

```bash
npm ci
npm run build
npm test
```
