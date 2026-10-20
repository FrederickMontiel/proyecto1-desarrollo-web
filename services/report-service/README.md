# Report Service

Genera los reportes PDF de SWMATRC con FastAPI, Jinja2 y WeasyPrint, y guarda su historial en MongoDB. Los PDF se generan solo en el backend (RNF-REP-01).

## Flujo

```text
POST /api/reports ──► PENDING ──► PROCESSING ──► COMPLETED
                                     │              (o FAILED, con el motivo)
                                     ├─ obtener datos (APIs internas de Community, Sensor y Alert, por lotes de 5000)
                                     ├─ preparar el documento (indicadores, tablas, barras)
                                     ├─ HTML con Jinja2
                                     ├─ PDF con WeasyPrint (en un hilo; máximo 2 a la vez por réplica)
                                     └─ PDF a GridFS y metadata a la colección "reportes"
```

La solicitud responde 202 con el `reportId` y el frontend consulta el estado. El servicio nunca lee las bases de los demás (sección 16) y no guarda nada en disco: cualquier réplica puede servir cualquier descarga.

Un reporte que queda en `PENDING` o `PROCESSING` más de 15 minutos se marca como fallido al arrancar el servicio. Eso pasa si la réplica que lo generaba se reinició a mitad del trabajo.

## Reportes

| Tipo | Requisito | Filtros |
|---|---|---|
| `COMMUNITIES` | RF-REP-01 Comunidades | `activa`, `departamento` |
| `SENSORS` | RF-REP-02 Sensores | `comunidadId`, `tipoSensor`, `estadoSensor` |
| `READINGS` | RF-REP-03 Lecturas | `sensorId`, `comunidadId`, `tipoSensor`, `desde`, `hasta` |
| `ALERTS` | RF-REP-04 Alertas | `comunidadId`, `sensorId`, `fenomeno`, `nivel`, `estado`, `desde`, `hasta` |
| `EVENT_HISTORY` | RF-REP-05 Historial de eventos | `comunidadId`, `fenomeno`, `nivel`, `estado`, `desde`, `hasta` |
| `SENSOR_STATUS` | RF-REP-06 Estado de sensores | `comunidadId` |
| `CLIMATE_STATS` | RF-REP-07 Estadísticas climáticas | `comunidadId`, `desde`, `hasta` |
| `EXECUTIVE` | RF-REP-08 Ejecutivo | `comunidadId`, `desde`, `hasta` |

Cada PDF lleva logo, encabezado en todas las páginas, nombre del reporte, fecha (hora de Guatemala), usuario, filtros aplicados, tablas con títulos que se repiten en cada hoja, numeración "Página X de Y" y pie de página (RNF-REP-03).

Las tablas muestran hasta 10 000 filas. Si hay más, el reporte avisa cuántas quedaron fuera para que el usuario acote los filtros (RNF-REP-05).

## Endpoints

| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/reports/types` | `reportes:generar` |
| POST | `/api/reports` — `{ "type": "ALERTS", "filters": { "comunidadId": 1 } }` | `reportes:generar` |
| GET | `/api/reports?type=&status=&pagina=&tamano=` (historial, RF-REP-09) | `reportes:generar` |
| GET | `/api/reports/{reportId}` | `reportes:generar` |
| GET | `/api/reports/{reportId}/download` (RF-REP-10) | `reportes:descargar` (solo administradores) |
| GET | `/api/reports/dashboard?comunidadId=&desde=&hasta=` | `comunidades:leer`, `sensores:leer` o `alertas:leer` |

En el historial, quien tiene `reportes:descargar` ve los reportes de todos; los demás ven solo los suyos. Un reporte ajeno responde 404, igual que uno que no existe. Cada descarga queda registrada en la metadata (`downloads`, `downloadCount`, `lastDownloadAt`) y en la auditoría.

## Metadata (MongoDB)

```json
{
  "reportId": "REP-2026-000001",
  "type": "ALERTS",
  "generatedBy": { "id": 1, "email": "admin@swmatrc.local", "name": "Administrador del sistema" },
  "generatedAt": "2026-10-23T00:41:31Z",
  "filters": { "comunidadId": 1 },
  "status": "COMPLETED",
  "fileName": "alertas-REP-2026-000001.pdf",
  "fileSize": 14816,
  "rows": 6,
  "downloadCount": 1,
  "lastDownloadAt": "2026-10-23T00:42:46Z"
}
```

## Desarrollo

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-dev.txt
.venv/Scripts/python -m pytest        # la prueba del PDF real se omite sin Pango
docker build -t swmatrc-report-service .   # corre todas, incluida la del PDF real
```
