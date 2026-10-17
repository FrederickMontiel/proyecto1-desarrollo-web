# Alert Service

Microservicio de alertas de SWMATRC, hecho con Java 25, Spring Boot 4.1 y PostgreSQL. Administra las reglas y los umbrales, evalúa el riesgo climático y lleva tanto las alertas como el historial de fenómenos: inundación, sequía, tormenta, helada e incendio forestal.

## Cómo funciona

Cada ciclo (3 s), Sensor Service manda a `POST /internal/evaluate` una fotografía por comunidad con todos sus sensores y su último valor. Con eso, Alert:

1. Clasifica cada sensor contra sus umbrales. Si un sensor no tiene umbrales calibrados, usa la plantilla de su tipo.
2. Ejecuta las **5 reglas integradas**, portadas de la Fase 2, y las **reglas configurables**. Deja un solo diagnóstico por fenómeno: el más grave.
3. Concilia con las alertas abiertas:
   - Si aparece un riesgo nuevo, abre una alerta.
   - Si un riesgo empeora, la escala y retira el acuse que tenía.
   - Si el riesgo desaparece, la cierra después de 5 ciclos seguidos en calma (histéresis).
4. Abre, actualiza o sella el evento del historial correspondiente.
5. Responde con el nivel de cada sensor y con los cambios, que Sensor difunde por SignalR.

Cuando un usuario atiende o cierra una alerta, Alert le avisa a Sensor (`/internal/notify`) para que los tableros se enteren al instante.

## Endpoints

| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/alerts` (filtros `comunidadId`, `sensorId`, `fenomeno`, `nivel`, `estado`, `desde`, `hasta`, `pagina`, `tamano`) | `alertas:leer` |
| GET | `/api/alerts/active?comunidadId=` | `alertas:leer` |
| GET | `/api/alerts/summary?comunidadId=&desde=&hasta=` | `alertas:leer` |
| GET | `/api/alerts/{id}` | `alertas:leer` |
| POST | `/api/alerts/{id}/attend`, `/acknowledge`, `/close` (cuerpo opcional `{comentario}`) | `alertas:atender` |
| GET | `/api/alerts/events`, `/api/alerts/events/stats` | `alertas:leer` |
| GET | `/api/alerts/rules`, `/api/alerts/rules/{id}` | `alertas:leer` |
| POST / PUT / PATCH `/status` / DELETE | `/api/alerts/rules…` | `reglas:editar` |
| GET | `/api/alerts/thresholds`, `/thresholds/templates`, `/thresholds/{sensorId}?tipo=` | `alertas:leer` |
| PUT / DELETE | `/api/alerts/thresholds/{sensorId}` | `reglas:editar` |
| POST | `/internal/evaluate`, `/internal/reset` | token de servicio |
| GET | `/internal/alerts/active`, `/internal/alerts`, `/internal/alerts/summary`, `/internal/events`, `/internal/events/stats` | token de servicio |
| GET | `/health/live`, `/health/ready` | — |

Las fechas van en ISO 8601 (`2026-10-22T00:00:00Z`). Los listados internos devuelven páginas de hasta 5000 filas, para que Report Service los recorra por lotes.

## Configuración

| Variable | Descripción |
|---|---|
| `DB_HOST`, `DB_PORT`, `DB_NOMBRE`, `DB_USUARIO`, `DB_PASSWORD` | Conexión a `alert_db`. |
| `JWT_JWKS`, `JWT_EMISOR`, `JWT_AUDIENCIA`, `JWT_AUDIENCIA_INTERNA` | Validación de los tokens del Auth Service. |
| `SERVICIO_SECRETO` | Credencial de `alert-service` para pedir su token interno. |
| `URL_AUTH`, `URL_AUDITORIA`, `URL_SENSOR` | Direcciones dentro de la red de Docker. |

## Desarrollo

No hace falta tener un JDK instalado: la imagen compila el proyecto y corre las pruebas.

```bash
docker build -t swmatrc-alert-service .
```

Las pruebas (JUnit, 42) cubren las reglas integradas, la clasificación por umbrales, el diagnóstico combinado y la conciliación.
