# SWMATRC — Sistema Web de Monitoreo y Alerta Temprana para Riesgos Climáticos

Aplicación web que simula un sistema de alerta temprana para comunidades rurales: recibe
telemetría meteorológica en tiempo real, la evalúa contra un motor de reglas de riesgo,
emite alertas por código de colores y conserva el historial de incidentes junto con la
bitácora de las acciones de los usuarios.

Toda la solución —base de datos, API y aplicación web— se ejecuta en contenedores Docker
sobre un servidor GNU/Linux sin interfaz gráfica.

**Documentación complementaria**

| Documento | Contenido |
|---|---|
| [docs/explicacion-del-sistema.md](docs/explicacion-del-sistema.md) | Recorrido del enunciado punto por punto: qué pide, dónde está resuelto y cómo funciona. Incluye el estado real de lo pendiente y un guion para la demostración. |
| [docs/arquitectura.md](docs/arquitectura.md) | Diagramas de despliegue, capas, ciclo de monitoreo y modelo entidad-relación. |

```bash
cd backend && dotnet test     # 54 pruebas: 35 del dominio, 19 de la capa de aplicación
```

---

## 1. Puesta en marcha

Requisitos: Docker Engine 24+ con Compose v2. Nada más: ni .NET ni Node hacen falta en la
máquina anfitriona.

```bash
git clone <url-del-repositorio> swmatrc
cd swmatrc

cp .env.example .env
# Editar .env: cambiar MSSQL_SA_PASSWORD y JWT_CLAVE antes de exponer el sistema.

docker compose up -d --build
```

La aplicación queda en **http://localhost:8080**.

El primer arranque tarda uno o dos minutos: SQL Server inicializa sus archivos, la API
espera a que el motor responda, aplica las migraciones y carga los datos iniciales.

```bash
docker compose ps          # estado de los tres servicios
docker compose logs -f api # seguir el arranque de la API
docker compose down        # detener (conserva los datos)
docker compose down -v     # detener y borrar la base de datos
```

### Cuentas de demostración

| Rol | Correo | Contraseña | Permisos |
|---|---|---|---|
| Administrador | `admin@swmatrc.org` | `Admin.2026` | Todo: sensores, cuentas de usuario, bitácora, reinicio |
| Operador | `operador@swmatrc.org` | `Operador.2026` | Reconocer alertas, activar sensores, fijar valores |
| Consulta | `consulta@swmatrc.org` | `Consulta.2026` | Solo lectura del tablero y del historial |

> Son credenciales de evaluación, cargadas por el sembrador únicamente cuando la base
> está vacía. En un despliegue real deben cambiarse en el primer acceso.

### Desarrollo local

La superposición de desarrollo publica hacia el anfitrión la API y SQL Server, que en
producción permanecen dentro de la red interna:

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d
```

- API: <http://localhost:5080> · OpenAPI en `/openapi/v1.json`
- SQL Server: `localhost,1433` (usuario `sa`)

Para trabajar en el frontend con recarga en caliente, con la API en Docker:

```bash
cd frontend
npm install
npm start          # http://localhost:4200, proxy hacia la API vía proxy.conf.json
```

---

## 2. Arquitectura

```
                       Navegador
                           │
              HTTP + WebSocket (mismo origen)
                           │
                  ┌────────▼────────┐
                  │   web (nginx)   │  SPA de Angular + proxy inverso
                  └────────┬────────┘
                           │  red interna de Docker
                  ┌────────▼────────┐
                  │  api (.NET 10)  │  REST + hub SignalR + simulador
                  └────────┬────────┘
                           │
                  ┌────────▼────────┐
                  │ db (SQL Server) │  volumen persistente
                  └─────────────────┘
```

Solo `web` publica un puerto. La API y la base de datos son alcanzables únicamente desde
la red interna, de modo que SQL Server nunca queda expuesto a Internet.

### Backend — arquitectura por capas

```
backend/src/
├── SWMatrc.Domain/          Entidades, enumerados y motor de riesgo. Sin dependencias.
│   └── Riesgo/Reglas/       Una clase por fenómeno: inundación, sequía, tormenta…
├── SWMatrc.Application/     Casos de uso, DTO y contratos (interfaces).
├── SWMatrc.Infrastructure/  EF Core, SQL Server, JWT, BCrypt, simulador climático.
└── SWMatrc.Api/             Controladores, hub SignalR, servicio en segundo plano.
```

Las dependencias apuntan siempre hacia adentro: `Api → Infrastructure → Application →
Domain`. El dominio no conoce ni EF Core ni ASP.NET, así que el motor de riesgo se puede
probar sin base de datos ni servidor.

Dos consecuencias prácticas de ese diseño:

- **Agregar un fenómeno nuevo** (deslizamiento, granizada) es escribir una clase que
  implemente `IReglaRiesgo` y registrarla en `DependencyInjection`. Ni el motor ni las
  reglas existentes cambian.
- **Sustituir la simulación por telemetría real** es implementar `ISimuladorClima` con el
  adaptador del datalogger. El servicio de monitoreo no se entera.

### Frontend

```
frontend/src/app/
├── core/
│   ├── modelos/        Tipos que reflejan los DTO de la API
│   ├── servicios/      Autenticación, cliente HTTP, canal en tiempo real, estado, avisos
│   ├── guardas/        Control de acceso por sesión y por rol
│   └── interceptores/  Inyección del token y traducción de errores
├── disposicion/        Armazón de la aplicación y pila de avisos
├── componentes/        Tarjeta de sensor, panel de alertas, gráfico, mapa
└── paginas/            Login, dashboard, alertas, historial, sensores, bitácora
```

Angular 21 con componentes autónomos, señales y sin `zone.js`. Cada página se carga de
forma diferida: el paquete inicial pesa unos 81 kB comprimidos.

`EstadoMonitoreo` es la única fuente de verdad del tablero. La carga inicial llega por
HTTP; a partir de ahí todo se actualiza con los mensajes del WebSocket, sin repreguntar a
la API.

---

## 3. Comunicación en tiempo real

El canal es un **WebSocket** gestionado con SignalR:

- El servidor acepta **exclusivamente** el transporte WebSocket
  (`options.Transports = HttpTransportType.WebSockets`) y el cliente omite la negociación
  previa (`skipNegotiation: true`). No hay degradación a long polling.
- La autenticación es el mismo JWT del resto de la API. Como el navegador no permite
  poner cabeceras en el handshake de un WebSocket, el token viaja en la cadena de
  consulta y se traslada al flujo normal de validación en el evento `OnMessageReceived`.
- Los mensajes se difunden **por grupo de comunidad**: un tablero abierto en una comunidad
  no recibe el tráfico de las demás. Es lo que permite añadir comunidades sin que crezca
  el ancho de banda de cada cliente.
- Reconexión automática con espera escalonada. Al reconectar, el cliente vuelve a
  suscribirse a su grupo, porque los grupos viven en la conexión y se pierden con ella.

Mensajes que emite el servidor:

| Mensaje | Cuándo | Contenido |
|---|---|---|
| `LecturaRecibida` | Cada ciclo, por sensor activo | Valor, unidad y nivel de la lectura |
| `AlertaGenerada` | Alerta nueva, agravada o reconocida | Alerta completa |
| `AlertaCerrada` | Las condiciones se normalizaron | Alerta cerrada |
| `EstadoSensorCambiado` | Alta, edición o cambio de estado | Sensor actualizado |
| `EstadoComunidad` | Al suscribirse a una comunidad | Instantánea completa |
| `SistemaReiniciado` | Reinicio del monitoreo | — |

La barra superior muestra el estado del canal. Si el WebSocket cae, el operador lo ve de
inmediato: a partir de ese momento el tablero muestra datos congelados, y en un sistema de
alerta temprana un canal caído sin avisar es peor que no tenerlo.

---

## 4. Motor de riesgo

Cada ciclo del simulador (3 s por omisión) genera una lectura por sensor activo, la
persiste y vuelve a evaluar el riesgo de cada comunidad.

Los umbrales **no están escritos en el código**: viven en cada sensor, así que un
administrador recalibra la red desde la interfaz sin recompilar nada.

| Fenómeno | Criterio |
|---|---|
| **Inundación** | Nivel del cauce contra sus umbrales. Lluvia intensa simultánea escala un peldaño. |
| **Tormenta** | Viento contra sus umbrales; viento y lluvia a la vez escalan un peldaño. |
| **Helada** | Temperatura contra sus umbrales inferiores. Humedad alta (escarcha) agrava. |
| **Sequía** | Concurrencia de lluvia escasa, aire seco, calor y cauce en mínimos: 2 señales → amarillo, 3 → naranja, 4 → rojo. |
| **Incendio forestal** | Índice 0–100 ponderado de temperatura, sequedad del aire y viento. Lluvia ≥ 5 mm lo suprime. |

Niveles según el protocolo: **Verde** (normal), **Amarillo** (precaución), **Naranja**
(alerta), **Rojo** (emergencia).

### Ciclo de vida de una alerta

Una alerta se levanta **una sola vez por episodio** y se mantiene abierta mientras la
condición persista: el operador ve un aviso por fenómeno, no una avalancha de
repeticiones cada tres segundos.

1. Se detecta el riesgo → se crea la alerta y se abre su asiento en el historial.
2. Si el episodio se agrava → sube de nivel, se retira el acuse de recibo anterior y se
   vuelve a avisar, porque lo que el operador reconoció ya no describe la situación.
3. Cuando las condiciones se normalizan → la alerta se **cierra**, no se borra, y el
   asiento del historial se sella con su fecha de fin.

### Simulación

El generador no saca números al azar en cada ciclo. Mantiene por comunidad un *episodio
climático* con duración —lluvia intensa, ola de calor, frente frío, sequía prolongada,
ventarrón— hacia el que convergen todos sus sensores. Así la lluvia sube junto con el
cauce y la humedad, y el motor de riesgo recibe situaciones plausibles en vez de ruido.

---

## 5. Base de datos

Siete tablas en SQL Server 2022, creadas por migraciones de EF Core al arrancar la API.

| Tabla | Contenido |
|---|---|
| `Usuarios` | Cuentas, hash BCrypt (factor 12) y rol. Correo único. |
| `Comunidades` | Localidades monitoreadas. Unidad de escalabilidad del sistema. |
| `Sensores` | Estaciones con su rango físico y sus umbrales de alerta. |
| `Lecturas` | Muestras. La tabla de mayor crecimiento, deliberadamente estrecha. |
| `Alertas` | Avisos emitidos, con su acuse de recibo y su fecha de cierre. |
| `EventosHistorial` | Asiento inmutable de cada episodio, para consulta e informes. |
| `Bitacora` | Pista de auditoría de las acciones de los usuarios. |

Decisiones de diseño que conviene señalar:

- **Índice filtrado** `IX_Alertas_Abiertas` sobre `(ComunidadId, FechaCierre)` con filtro
  `FechaCierre IS NULL`. El tablero consulta constantemente las alertas abiertas, que son
  una fracción mínima del total acumulado.
- **Índice descendente** en `Lecturas(SensorId, FechaHora DESC)`: cubre exactamente el
  patrón de consulta de los gráficos sobre la tabla más grande.
- **Desnormalización deliberada**: `Sensores.ValorActual` guarda la última medición para
  que el tablero y el motor de riesgo no recorran el histórico en cada ciclo;
  `EventosHistorial.OrigenSensor` y `Bitacora.UsuarioEmail` copian el nombre para que el
  registro siga siendo legible aunque el sensor o la cuenta desaparezcan.
- **Borrado en cascada acotado**: solo desde `Comunidades`. Las claves foráneas de
  `Alertas → Sensores` y `EventosHistorial → Alertas` van sin acción, tanto porque borrar
  evidencia por un cambio de inventario sería inaceptable, como porque SQL Server rechaza
  dos rutas de cascada hacia la misma tabla. La baja de un sensor desata sus referencias
  de forma explícita en el servicio correspondiente.

---

## 6. Seguridad

- **Autenticación** con JWT firmado en HMAC-SHA256. La clave se inyecta por variable de
  entorno y la API se niega a arrancar si mide menos de 32 caracteres.
- **Contraseñas** con BCrypt, factor de trabajo 12. Un hash corrupto se trata como
  credencial inválida, no como error del servidor.
- **Autorización por rol** mediante políticas: `Operacion` (operador y administrador) y
  `Administracion` (solo administrador). Se aplican en los controladores.
- **Mensajes de error uniformes** en el inicio de sesión: la respuesta es idéntica para un
  correo inexistente y para una contraseña incorrecta, de modo que no se puede averiguar
  qué correos están registrados.
- **Bitácora** escrita siempre desde el servidor. No se expone ninguna operación de
  modificación ni de borrado sobre ella.
- **Contenedores sin privilegios**: la API corre como el usuario `app` de la imagen base,
  no como root.
- **Superficie mínima**: solo el contenedor web publica un puerto.

---

## 7. Funcionalidades

**Monitoreo** — Temperatura, humedad relativa, velocidad del viento, nivel de lluvia y
nivel del río, actualizados en vivo. Cada tarjeta sitúa la lectura en el rango del
instrumento con los umbrales marcados encima: se ve cuánto falta para el siguiente color.

**Alertas** — Detección automática, código de cuatro colores, mensaje descriptivo con
recomendación para el operador, aviso visual y sonoro, y acuse de recibo nominal.

El sonido se sintetiza con la Web Audio API en vez de reproducir un archivo: no hay nada
que descargar, el aviso suena igual sin conexión —clave en una comunidad rural— y la
urgencia se codifica en el tono, que sube en frecuencia, volumen y número de repeticiones
con el nivel.

**Historial** — Episodios con fecha de inicio, fecha de fin, duración, tipo de fenómeno,
nivel máximo alcanzado y sensor de origen. Filtros por fenómeno y por rango de fechas.

**Administración** — Alta de sensores con calibración de fábrica según el tipo, edición de
umbrales, activación y desactivación, inyección manual de lecturas para ensayar escenarios
de alerta, y reinicio del monitoreo.

**Dashboard** — Indicadores principales, gráficos de evolución con los umbrales trazados
sobre la curva, y croquis de la comunidad con el estado de cada sensor.

El mapa se dibuja como SVG propio en lugar de usar teselas remotas: el sistema está
pensado para zonas con conexión intermitente, y una vista que depende de un servidor
externo se queda en blanco justo cuando más se necesita.

---

## 8. Despliegue en VPS

Servidor GNU/Linux sin interfaz gráfica, con Docker Engine y Compose v2.

```bash
# 1. Código y configuración
git clone <url-del-repositorio> /opt/swmatrc
cd /opt/swmatrc
cp .env.example .env

# 2. Credenciales aleatorias
sed -i "s|^JWT_CLAVE=.*|JWT_CLAVE=$(openssl rand -base64 48)|" .env
sed -i "s|^MSSQL_SA_PASSWORD=.*|MSSQL_SA_PASSWORD=$(openssl rand -base64 24)|" .env
chmod 600 .env

# 3. Arranque
docker compose up -d --build
```

Publicar la aplicación con TLS mediante un proxy inverso (Caddy resuelve el certificado
solo):

```caddy
monitoreo.ejemplo.org {
    reverse_proxy localhost:8080
}
```

Con TLS activo, ajustar en `.env`:

```
CORS_ORIGEN=https://monitoreo.ejemplo.org
```

Cortafuegos: abrir 80 y 443. **No** abrir 1433 ni 5080; con el `docker-compose.yml` de
producción esos puertos ni siquiera se publican.

Respaldo de la base de datos:

```bash
docker compose exec db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "BACKUP DATABASE SwmatrcDb TO DISK='/var/opt/mssql/data/swmatrc.bak' WITH FORMAT"

docker compose cp db:/var/opt/mssql/data/swmatrc.bak ./respaldos/
```

---

## 9. Configuración

Todas las variables se leen de `.env`.

| Variable | Descripción | Por omisión |
|---|---|---|
| `MSSQL_SA_PASSWORD` | Contraseña de `sa`. Mínimo 8 caracteres con mayúsculas, minúsculas y dígitos. | — |
| `MSSQL_DATABASE` | Nombre de la base de datos. | `SwmatrcDb` |
| `JWT_CLAVE` | Clave de firma de los tokens. Mínimo 32 caracteres. | — |
| `JWT_MINUTOS_VIGENCIA` | Vigencia del token. | `480` |
| `SIMULACION_INTERVALO_SEGUNDOS` | Periodo del ciclo de adquisición. | `3` |
| `PUERTO_WEB` | Puerto publicado en el anfitrión. | `8080` |
| `CORS_ORIGEN` | Origen autorizado del frontend. | `http://localhost:8080` |

La API acepta además cualquier clave de `appsettings.json` como variable de entorno, con
doble guion bajo para anidar: `Simulacion__Habilitada=false` detiene el simulador sin
tocar el código.

---

## 10. API

Todos los extremos exigen `Authorization: Bearer <token>`, salvo `/api/cuenta/login` y
`/health`.

| Método | Ruta | Rol | Descripción |
|---|---|---|---|
| `POST` | `/api/cuenta/login` | — | Inicio de sesión |
| `GET` | `/api/cuenta/yo` | Cualquiera | Identidad del token |
| `GET/POST` | `/api/cuenta/usuarios` | Administrador | Listar y crear usuarios |
| `PUT` | `/api/cuenta/usuarios/{id}` | Administrador | Cambiar nombre y rol |
| `PATCH` | `/api/cuenta/usuarios/{id}/estado` | Administrador | Habilitar o deshabilitar |
| `POST` | `/api/cuenta/usuarios/{id}/password` | Administrador | Restablecer contraseña |
| `GET` | `/api/monitoreo/comunidades` | Cualquiera | Comunidades registradas |
| `GET` | `/api/monitoreo/comunidades/{id}/estado` | Cualquiera | Instantánea del tablero |
| `GET` | `/api/monitoreo/comunidades/{id}/series` | Cualquiera | Series para los gráficos |
| `GET` | `/api/monitoreo/resumen` | Cualquiera | Indicadores agregados |
| `GET` | `/api/sensores` | Cualquiera | Inventario de sensores |
| `GET` | `/api/sensores/plantillas` | Cualquiera | Calibraciones de fábrica |
| `POST/PUT/DELETE` | `/api/sensores[/{id}]` | Administrador | Alta, edición y baja |
| `PATCH` | `/api/sensores/{id}/estado` | Operador | Activar o desactivar |
| `POST` | `/api/sensores/{id}/valor` | Operador | Fijar una lectura manual |
| `GET` | `/api/alertas`, `/api/alertas/activas` | Cualquiera | Consulta de alertas |
| `POST` | `/api/alertas/{id}/reconocer` | Operador | Acuse de recibo |
| `GET` | `/api/historial` | Cualquiera | Historial de eventos |
| `GET` | `/api/bitacora` | Administrador | Pista de auditoría |
| `POST` | `/api/sistema/reiniciar` | Administrador | Reiniciar el monitoreo |
| `GET` | `/health` | — | Sonda de estado |

---

## 11. Tecnologías

| Componente | Tecnología |
|---|---|
| Frontend | Angular 21 · componentes autónomos, señales, sin `zone.js` · Chart.js |
| Backend | .NET 10 (LTS) · ASP.NET Core · SignalR sobre WebSockets |
| Datos | SQL Server 2022 · Entity Framework Core 10 |
| Seguridad | JWT (HMAC-SHA256) · BCrypt |
| Servidor web | nginx 1.27 (SPA y proxy inverso) |
| Contenedores | Docker · Compose v2 |

> Nota sobre la versión: **.NET Core** dejó de existir como nombre en la versión 3.1.
> Desde .NET 5 (2020) la plataforma se llama simplemente **.NET**, y .NET 10 es la
> versión LTS actual. Los paquetes conservan «Core» en su nombre por continuidad
> (`Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`), pero el destino de
> compilación es `net10.0`.

---

## 12. Resolución de problemas

**La API reintenta conectarse a la base de datos al arrancar.** Es normal en el primer
inicio: SQL Server tarda en inicializar sus archivos. La API reintenta doce veces con
cinco segundos de espera. Si persiste, revisar `docker compose logs db`.

**`PAL initialization failed. Error: 101` en el contenedor de la base.** El volumen quedó
a medio escribir por una interrupción del primer arranque. Se resuelve recreándolo:
`docker compose down -v && docker compose up -d`.

**El indicador de conexión se queda en «Reconectando…».** El proxy inverso no está
reenviando la actualización a WebSocket. Verificar que las cabeceras `Upgrade` y
`Connection` lleguen hasta la API (ver `frontend/nginx.conf`).

**Las alertas no suenan.** Los navegadores bloquean el audio hasta que hay un gesto del
usuario. El contexto se habilita al iniciar sesión; si se silenció a propósito, se
restablece con el botón de la campana en la barra superior.
