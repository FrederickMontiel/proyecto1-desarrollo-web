# Explicación del sistema, requisito por requisito

Este documento recorre el enunciado del Proyecto I en el mismo orden en que está escrito y,
para cada punto, responde tres preguntas: **qué pide**, **dónde está resuelto** y **cómo
funciona**. Sirve como guía de lectura del código y como apoyo para la defensa del proyecto.

Complementa a:

- [README.md](../README.md) — puesta en marcha, credenciales y despliegue.
- [arquitectura.md](arquitectura.md) — diagramas, modelo de datos y decisiones de diseño.

**Leyenda de estado:** ✅ implementado · 🟡 parcial · ⬜ pendiente

---

## 0. El objetivo, en una frase

> «Simular un sistema de monitoreo y alerta temprana para riesgos climáticos en comunidades
> rurales.»

Un sistema de alerta temprana no es un tablero de números bonitos. Es una cadena de cuatro
eslabones, y el enunciado los pide todos:

1. **Adquirir** la medición → los cinco sensores.
2. **Evaluar** si hay peligro → el motor de riesgo.
3. **Avisar** a quien puede actuar → alertas con código de colores, mensaje y sonido.
4. **Dejar constancia** → historial de eventos y bitácora.

La arquitectura del proyecto está organizada alrededor de esa cadena, no alrededor de las
pantallas. Por eso el motor de riesgo vive en el dominio, aislado de la base de datos y del
servidor web, y puede probarse solo.

---

## 1. Requisitos funcionales

### 1.1 Monitoreo climático

| # | Requisito | Estado | Dónde |
|---|---|---|---|
| 1 | Mostrar temperatura ambiente | ✅ | `TipoSensor.Temperatura` |
| 2 | Mostrar humedad relativa | ✅ | `TipoSensor.Humedad` |
| 3 | Mostrar velocidad del viento | ✅ | `TipoSensor.Viento` |
| 4 | Mostrar nivel de lluvia | ✅ | `TipoSensor.Lluvia` |
| 5 | Mostrar nivel de un río o reservorio | ✅ | `TipoSensor.NivelRio` |
| 6 | Actualizar en tiempo real (simulada) | ✅ | Ciclo de 3 s + WebSocket |

**Las cinco magnitudes** son un enumerado en
[Enums.cs:4](../backend/src/SWMatrc.Domain/Enums/Enums.cs#L4). Cada comunidad se siembra con
un sensor de cada tipo.

Un detalle de diseño que conviene explicar: **el sistema no tiene cinco clases de sensor,
tiene una**. `Sensor` describe cualquier instrumento mediante su tipo, su rango físico y sus
umbrales. Un termómetro y un limnímetro se diferencian solo por sus datos, no por su código.
Eso es lo que hace posible el requisito de escalabilidad más abajo.

**La calibración de cada tipo** está en
[PlantillaSensor.cs](../backend/src/SWMatrc.Domain/Riesgo/PlantillaSensor.cs): unidad, rango
y umbrales recomendados. Los valores no son inventados — siguen las escalas de aviso de los
servicios meteorológicos: viento en escala Beaufort (39 / 62 / 89 km/h), precipitación por
intensidad horaria, y nivel de cauce respecto a la cota de desbordamiento (4,5 m).

**En la interfaz**, cada magnitud es una tarjeta:
[dashboard.html:89](../frontend/src/app/paginas/dashboard/dashboard.html#L89) →
[tarjeta-sensor](../frontend/src/app/componentes/tarjeta-sensor/tarjeta-sensor.html).

La tarjeta no se limita a mostrar el número. Dibuja una barra con la lectura situada dentro
del rango del instrumento y **marca los umbrales encima**. Así el operador no ve solo
«28 °C», ve cuánto le falta para el siguiente color. Esa es la diferencia entre un dato y una
lectura accionable.

#### El punto 6: tiempo real de verdad

El enunciado dice «actualizar la información en tiempo real (simulada)». Hay dos formas de
cumplirlo, y solo una es defendible:

- **Sondeo** (`setInterval` pidiendo a la API cada N segundos): sencillo, pero el cliente
  pregunta a ciegas y siempre llega tarde.
- **Empuje desde el servidor**: el servidor avisa en cuanto tiene el dato.

Aquí se usa lo segundo, con un **WebSocket** gestionado por SignalR:

```
ServicioSimulacion (BackgroundService, cada 3 s)
        │  ServicioSimulacion.cs:42  → PeriodicTimer
        ▼
ServicioMonitoreo.EjecutarCicloAsync()          ServicioMonitoreo.cs:32
        │  genera lectura → persiste en lote → evalúa riesgo
        ▼
INotificadorTiempoReal → MonitoreoHub → WebSocket → navegador
```

- El reloj: [ServicioSimulacion.cs:42](../backend/src/SWMatrc.Api/Trabajos/ServicioSimulacion.cs#L42).
  `PeriodicTimer` no acumula retrasos: si un ciclo tarda de más, el siguiente sale a su hora.
- El ciclo: [ServicioMonitoreo.cs:32](../backend/src/SWMatrc.Application/Servicios/ServicioMonitoreo.cs#L32).
- El transporte queda **restringido a WebSockets** en
  [Program.cs:166](../backend/src/SWMatrc.Api/Program.cs#L166). No hay degradación a
  *long polling*: o el canal es un socket permanente, o no hay canal.

En el navegador, [`EstadoMonitoreo`](../frontend/src/app/core/servicios/estado-monitoreo.ts)
es la única fuente de verdad del tablero: la carga inicial llega por HTTP y **a partir de ahí
todo se actualiza con los mensajes del socket**, sin volver a consultar la API.

> **Comprobado en ejecución:** 15 lecturas empujadas por el servidor en 9 segundos
> (5 sensores × 3 ciclos), con transporte `WebSocketTransport` confirmado por el cliente.

#### Cuando un sensor deja de reportar

Una estación de campo no siempre responde: se queda muda por avería, batería agotada o
pérdida del enlace. El sistema distingue **tres** estados, no dos:

| Estado | Significado | Qué se ve |
|---|---|---|
| `Activo` | En servicio y reportando | Tarjeta normal con su nivel de riesgo |
| `Inactivo` | Alguien lo apagó a propósito | Tarjeta atenuada, «Fuera de servicio» |
| `SinSenal` | En servicio pero mudo: está averiado | Borde discontinuo ámbar, «⚠ Sin señal» |

La diferencia entre los dos últimos es la que importa. Un sensor apagado es una decisión;
uno sin señal es un fallo. Mostrar el segundo como si todo fuera bien, con su última
lectura congelada, transmitiría una calma que el sistema ya no puede garantizar.

El mecanismo tiene dos piezas:

1. **El simulador falla a veces** — con probabilidad baja, un instrumento deja de reportar
   durante unos ciclos ([SimuladorClimaEnMemoria](../backend/src/SWMatrc.Infrastructure/Simulacion/SimuladorClimaEnMemoria.cs)).
   `ISimuladorClima.SiguienteValor` devuelve `decimal?`: `null` es silencio, no cero.
2. **El ciclo lo detecta** — si el silencio supera la tolerancia (tres ciclos por omisión),
   el sensor pasa a `SinSenal`, sale del motor de riesgo y el cambio se difunde por el
   WebSocket. Al volver a reportar, recupera `Activo` solo.

Nunca se inventa una lectura para rellenar el hueco: si el instrumento no respondió, no hay
muestra que persistir ni que graficar.

El tablero muestra un indicador ámbar de **«Sin señal»**, que aparece únicamente cuando hay
algún sensor mudo. Un sistema de alerta temprana debe poder decir *«ya no sé lo que ocurre
ahí»*, no solo *«hay peligro»* o *«no lo hay»*.

---

### 1.2 Generación de alertas

| # | Requisito | Estado | Dónde |
|---|---|---|---|
| 1 | Detectar automáticamente condiciones de riesgo | ✅ | `MotorRiesgo` + 5 reglas |
| 2–6 | Niveles Verde / Amarillo / Naranja / Rojo | ✅ | [Enums.cs:17](../backend/src/SWMatrc.Domain/Enums/Enums.cs#L17) |
| 7 | Mensajes descriptivos del riesgo | ✅ | Cada regla redacta el suyo |
| 8 | Notificación visual y/o sonora | ✅ | Ambas |

#### Cómo se detecta el riesgo

El motor no es un `if` gigante. Cada fenómeno es **una clase independiente** que implementa
[`IReglaRiesgo`](../backend/src/SWMatrc.Domain/Riesgo/IReglaRiesgo.cs):

| Fenómeno | Archivo | Prioridad | Criterio |
|---|---|---|---|
| Inundación | [ReglaInundacion.cs](../backend/src/SWMatrc.Domain/Riesgo/Reglas/ReglaInundacion.cs) | 1 | Nivel del cauce; lluvia intensa simultánea escala un peldaño |
| Tormenta | [ReglaTormenta.cs](../backend/src/SWMatrc.Domain/Riesgo/Reglas/ReglaTormenta.cs) | 2 | Viento; viento + lluvia a la vez escalan un peldaño |
| Helada | [ReglaHelada.cs](../backend/src/SWMatrc.Domain/Riesgo/Reglas/ReglaHelada.cs) | 3 | Temperatura contra umbrales **inferiores**; humedad alta agrava (escarcha) |
| Sequía | [ReglaSequia.cs](../backend/src/SWMatrc.Domain/Riesgo/Reglas/ReglaSequia.cs) | 4 | Concurrencia de 4 señales: 2 → amarillo, 3 → naranja, 4 → rojo |
| Incendio forestal | [ReglaIncendioForestal.cs](../backend/src/SWMatrc.Domain/Riesgo/Reglas/ReglaIncendioForestal.cs) | 5 | Índice 0–100 ponderado; lluvia ≥ 5 mm lo suprime |

[`MotorRiesgo`](../backend/src/SWMatrc.Domain/Riesgo/MotorRiesgo.cs) recibe las reglas por
inyección de dependencias, las ejecuta todas y devuelve los diagnósticos **ordenados de mayor
a menor severidad**.

Consecuencia práctica: **añadir un fenómeno nuevo** (deslizamiento, granizada) es escribir
una clase y añadir una línea en
[DependencyInjection.cs](../backend/src/SWMatrc.Application/DependencyInjection.cs). Ni el
motor ni las reglas existentes se tocan.

Dos reglas merecen explicación aparte porque no son simples comparaciones:

**Sequía** — ninguna magnitud aislada define una sequía. Se puntúa la concurrencia de lluvia
escasa, aire seco, calor y cauce en mínimos; el total decide la severidad. Es una
aproximación al criterio multivariable que usan los índices de sequía reales.

**Incendio forestal** — se calcula un índice inspirado en los sistemas de peligro
meteorológico de incendios (tipo FWI): el calor y el aire seco preparan el combustible
(pesos 0,35 y 0,40), el viento lo propaga (0,25), y la lluvia reciente lo suprime por
completo. El combustible fino mojado no arde por mucho calor y viento que haga.

#### Los cuatro niveles

[`NivelAlerta`](../backend/src/SWMatrc.Domain/Enums/Enums.cs#L17) es un enumerado **ordenado**
(`Verde = 0 … Rojo = 3`). Que el orden sea significativo permite comparar severidades con
`>` y obtener el nivel global de la comunidad con un simple `Max`.

La clasificación de una lectura contra los umbrales del sensor está en
[Sensor.Clasificar():64](../backend/src/SWMatrc.Domain/Entities/Sensor.cs#L64). Evalúa **de
rojo hacia amarillo** para quedarse siempre con el peor caso.

Cada sensor lleva **dos escaleras de umbrales**:

- *Por exceso*: el riesgo sube cuando el valor sube (viento, cauce, calor).
- *Por defecto*: el riesgo sube cuando el valor baja (helada, sequía).

Un termómetro usa las dos: calor extremo por arriba, helada por abajo.

#### Mensajes descriptivos

Cada regla redacta su propio mensaje, y el mensaje **incluye la recomendación**, no solo el
diagnóstico. Ejemplo real generado por el sistema:

> «EMERGENCIA por inundación: el cauce alcanzó 4,87 m, por encima del nivel de
> desbordamiento. Evacuar de inmediato las viviendas ribereñas.»

Un aviso que dice «nivel rojo» obliga al operador a saber qué hacer. Un aviso que dice qué
hacer sirve a quien lo lee a las tres de la mañana.

#### Notificación visual y sonora

El enunciado admite «y/o». Están las dos.

**Visual** — tres capas, de más a menos intrusiva:

1. Franja de situación al ancho del tablero, teñida del color del nivel
   ([dashboard.html:3](../frontend/src/app/paginas/dashboard/dashboard.html#L3)).
2. Aviso emergente
   ([avisos.ts](../frontend/src/app/disposicion/avisos/avisos.ts)), anunciado como región
   activa (`aria-live="assertive"`) para lectores de pantalla. Las emergencias permanecen
   hasta que alguien las cierra; el resto se retira solo.
3. Contador de alertas sin reconocer en la navegación lateral.

**Sonora** — [notificaciones.ts:87](../frontend/src/app/core/servicios/notificaciones.ts#L87).

El sonido **se sintetiza con la Web Audio API**, no se reproduce un archivo. Tres razones:

- No hay ningún recurso que descargar.
- Suena igual sin conexión, lo que importa en una comunidad rural.
- La urgencia se codifica en el propio tono: a mayor nivel, mayor frecuencia, más volumen y
  más repeticiones ([tabla `TONOS`](../frontend/src/app/core/servicios/notificaciones.ts#L13)).

Los navegadores bloquean el audio hasta que hay un gesto del usuario, así que el contexto se
habilita al iniciar sesión.

#### Ciclo de vida de una alerta

Aquí está la decisión de diseño más importante de todo el proyecto, y conviene poder
explicarla:

Una alerta se levanta **una sola vez por episodio** y permanece abierta mientras la condición
persista. El operador ve un aviso por fenómeno, no una avalancha de repeticiones cada tres
segundos.

1. Se detecta el riesgo → se crea la alerta y se abre su asiento en el historial.
2. Si el episodio se agrava → sube de nivel, **se retira el acuse de recibo anterior** y se
   vuelve a avisar. Lo que el operador reconoció ya no describe la situación.
3. Cuando se normaliza → la alerta se **cierra**, no se borra.

**Histéresis en el cierre.** Una alerta no se retira en cuanto la lectura baja del umbral:
debe mantenerse en normalidad **cinco evaluaciones consecutivas** (unos quince segundos). El
contador vive en `Alertas.CiclosSinRiesgo`.

Esto no es una precaución teórica. En la primera ejecución del sistema, **sin** histéresis, el
motor generó **56 eventos en un minuto**: el ruido de la medición cruzaba el umbral arriba y
abajo, abriendo y cerrando alertas sin parar. Con la histéresis, la misma simulación produjo
**3 eventos en dos minutos**, con duraciones de 53 a 65 segundos.

La **apertura**, en cambio, es inmediata. En alerta temprana un falso positivo cuesta mucho
menos que un aviso tardío.

---

### 1.3 Historial de eventos

| # | Requisito | Estado | Dónde |
|---|---|---|---|
| 1 | Registrar cada alerta generada | ✅ | `EventosHistorial` |
| 2 | Mostrar fecha y hora del evento | ✅ | `FechaInicio` / `FechaFin` / duración |
| 3 | Mostrar el tipo de fenómeno (5) | ✅ | [Enums.cs:26](../backend/src/SWMatrc.Domain/Enums/Enums.cs#L26) |

El sistema mantiene **dos tablas** donde a primera vista bastaría una, y la distinción es
deliberada:

| | `Alertas` | `EventosHistorial` |
|---|---|---|
| Qué es | Estado **vivo** del sistema | Asiento **inmutable** del incidente |
| Se consulta para | Saber qué pasa ahora | Consultar e informar después |
| Ciclo | Se abre, escala, se cierra | Se abre y se sella |
| Si se depuran alertas viejas | Desaparece | **Sobrevive** |

`EventosHistorial` copia el nombre del sensor en `OrigenSensor` en lugar de referenciarlo. Así
el historial sigue siendo legible aunque el sensor se dé de baja más adelante. Es
desnormalización deliberada, no descuido.

**La vista**: [historial](../frontend/src/app/paginas/historial/historial.html) muestra inicio,
fin, duración legible, fenómeno con icono, nivel máximo alcanzado, sensor de origen y
descripción. Filtros por fenómeno y por rango de fechas, con paginación en servidor.

Un detalle de UX: los episodios que siguen abiertos se muestran como **«En curso»** en lugar
de una duración vacía.

---

### 1.4 Administración

| # | Requisito | Estado | Dónde |
|---|---|---|---|
| 1 | Agregar nuevos sensores simulados | ✅ | `POST /api/sensores` |
| 2 | Editar los valores de los sensores | ✅ | Dos sentidos: valor y calibración |
| 3 | Activar o desactivar sensores | ✅ | `PATCH /api/sensores/{id}/estado` |
| 4 | Reiniciar el sistema de monitoreo | ✅ | `POST /api/sistema/reiniciar` |
| — | *(cuentas de usuario)* | ✅ | [usuarios](../frontend/src/app/paginas/usuarios/usuarios.html) |

Todo vive en [sensores](../frontend/src/app/paginas/sensores/sensores.html) y
[SensoresController.cs](../backend/src/SWMatrc.Api/Controllers/SensoresController.cs).

**Agregar** — El formulario propone la calibración de fábrica en cuanto se elige el tipo:
unidad, rango, variación y las dos escaleras de umbrales. Dar de alta un sensor puede
reducirse a elegir tipo y nombre. La API expone el catálogo en
`GET /api/sensores/plantillas` ([línea 29](../backend/src/SWMatrc.Api/Controllers/SensoresController.cs#L29)).

**Editar los valores** — El enunciado admite dos lecturas y se implementan las dos:

- *Fijar la lectura actual* (`POST /{id}/valor`): inyecta una medición concreta. Sirve para
  **ensayar escenarios de alerta** durante una demostración, sin esperar a que la simulación
  llegue sola al umbral. La lectura queda en el histórico igual que una simulada: el motor de
  riesgo no distingue su origen.
- *Editar la calibración* (`PUT /{id}`): cambia rango y umbrales. Como recalibrar puede abrir
  o cerrar alertas, el servicio **reevalúa la comunidad de inmediato**.

Ambas rutas validan antes de escribir: valor dentro del rango físico, mínimo menor que máximo,
y la escalera de umbrales coherente (amarillo < naranja < rojo por exceso; al revés por
defecto). Un intento fuera de rango responde `400` con `ProblemDetails` en español.

**Activar o desactivar** — Un sensor inactivo deja de generar lecturas y **queda fuera del
motor de riesgo** ([ContextoEvaluacion](../backend/src/SWMatrc.Domain/Riesgo/ContextoEvaluacion.cs)
filtra por `EstaOperativo`). Apagar un sensor puede, por tanto, cerrar una alerta; encenderlo
puede levantarla. Por eso el servicio reevalúa tras el cambio.

**Reiniciar** — [ServicioMonitoreo.cs:376](../backend/src/SWMatrc.Application/Servicios/ServicioMonitoreo.cs#L376).
Reservado al administrador. Qué hace y qué **no** hace:

| Acción | Qué ocurre | Por qué |
|---|---|---|
| Alertas abiertas | Se **cierran**, no se borran | Son evidencia |
| Asientos de historial abiertos | Se sellan con fecha de fin | El incidente ocurrió |
| Lecturas acumuladas | Se **descartan** | Son datos simulados; los gráficos arrancan limpios |
| Sensores | Vuelven a su valor de reposo y a estado activo | Es lo que se espera de un reinicio |
| Estado del simulador | Se descarta el episodio en curso | Empieza de cero |

La interfaz pide confirmación explicando exactamente eso antes de ejecutarlo.

#### Cuentas de usuario

El enunciado exige persistir usuarios; administrarlos es la consecuencia natural. La
pantalla permite dar de alta, cambiar el nombre y el rol, restablecer la contraseña y
habilitar o deshabilitar cuentas.

**Las cuentas no se eliminan, se deshabilitan.** Borrarlas dejaría huérfanas las alertas
que esa persona reconoció y los asientos de bitácora que llevan su nombre.

Tres salvaguardas impiden dejar la instalación sin quien la administre. Se aplican **en el
servidor**, no solo deshabilitando botones:

| Regla | Motivo |
|---|---|
| Nadie puede deshabilitar su propia cuenta | Un clic accidental cerraría la sesión de forma irreversible |
| Nadie puede retirarse a sí mismo el rol de administrador | El token en curso dejaría de corresponder a los permisos reales |
| El último administrador activo no puede perder el rol ni ser deshabilitado | Sin él, nadie podría volver a entrar a administrar |

Una cuenta *deshabilitada* con rol de administrador **no cuenta como relevo**: nadie podría
iniciar sesión con ella.

El correo es inmutable. Es la credencial de acceso y la referencia con la que la bitácora
identifica a quien hizo cada cosa; cambiarlo rompería esa trazabilidad.

---

### 1.5 Dashboard

| # | Requisito | Estado | Dónde |
|---|---|---|---|
| 1 | Indicadores principales | ✅ | [dashboard.html:43](../frontend/src/app/paginas/dashboard/dashboard.html#L43) |
| 2 | Gráficos con la evolución de los datos | ✅ | [dashboard.html:124](../frontend/src/app/paginas/dashboard/dashboard.html#L124) |
| 3 | Mapa de la comunidad *(opcional)* | ✅ | [dashboard.html:111](../frontend/src/app/paginas/dashboard/dashboard.html#L111) |

**Indicadores** — sensores en servicio, alertas activas, alertas sin reconocer y eventos de
las últimas 24 h. Las cifras usan **cifras de ancho fijo** (`font-variant-numeric:
tabular-nums`); sin eso el número «salta» en cada actualización y el tablero parece inestable.

**Gráficos** — [grafico-evolucion](../frontend/src/app/componentes/grafico-evolucion/grafico-evolucion.ts),
sobre Chart.js con registro selectivo de componentes para no inflar el paquete.

Se grafica **una magnitud a la vez, con su unidad real**. Superponer °C, mm y km/h en un mismo
eje daría una imagen vistosa e ilegible. Sobre la curva se trazan **los umbrales del sensor**
como rectas discontinuas, que es lo que permite leer la tendencia en términos de riesgo y no
solo de número.

Las animaciones están desactivadas: con una lectura cada tres segundos, la transición haría
que la curva pareciera moverse sola todo el tiempo.

**Mapa** — [mapa-comunidad](../frontend/src/app/componentes/mapa-comunidad/mapa-comunidad.ts).

Es un **SVG propio**, no un mapa con teselas remotas. La razón es de fondo: el sistema está
pensado para zonas con conexión intermitente, y una vista que depende de un servidor de
teselas externo se queda en blanco justo cuando más se necesita. Las coordenadas reales se
proyectan sobre el lienzo, de modo que las posiciones relativas entre estaciones son
correctas. Los sensores en emergencia llevan un halo pulsante; se reserva la animación para el
rojo para que llame la atención de verdad cuando aparece.

---

## 2. Requisitos no funcionales

### 2.1 Usabilidad ✅

- Navegación de cinco secciones, sin submenús.
- El estado global de la comunidad se lee **desde el otro extremo de la sala**: franja de
  color a todo lo ancho.
- Cada alerta lleva su acción a un clic (**Reconocer**).
- Las operaciones destructivas explican sus consecuencias antes de ejecutarse.
- Selector de comunidad siempre visible en la barra superior.

### 2.2 Compatibilidad 🟡

El proyecto compila con la línea base de navegadores de Angular 21, que cubre Chrome, Edge,
Firefox y Safari en versiones actuales. No se usa ninguna API exclusiva de un motor.

**Pendiente:** no se ha probado manualmente en los cuatro navegadores. Es una verificación,
no una tarea de desarrollo.

### 2.3 Rendimiento ✅

- El servidor **empuja** los datos; el cliente no sondea.
- Un único `SaveChanges` por ciclo: las lecturas se insertan en lote.
- `Sensores.ValorActual` guarda la última medición para que el tablero y el motor no recorran
  el histórico en cada ciclo.
- Índice filtrado `IX_Alertas_Abiertas` para la consulta más frecuente del sistema.
- Las páginas se cargan de forma diferida: el paquete inicial pesa **81 kB comprimidos**.
- Angular **sin `zone.js`**, con señales: solo se repinta lo que cambió.

### 2.4 Seguridad ✅

| Medida | Dónde |
|---|---|
| JWT firmado en HMAC-SHA256 | [ServiciosSeguridad.cs](../backend/src/SWMatrc.Infrastructure/Seguridad/ServiciosSeguridad.cs) |
| La API no arranca si la clave mide < 32 caracteres | [Program.cs:65](../backend/src/SWMatrc.Api/Program.cs#L65) |
| Contraseñas con BCrypt, factor 12 | `HasheadorPasswordBCrypt` |
| Autorización por políticas de rol | [Program.cs:104](../backend/src/SWMatrc.Api/Program.cs#L104) |
| Bitácora escrita solo desde el servidor | `ServicioBitacora` |
| Contenedores sin privilegios | `USER $APP_UID` en el Dockerfile |
| Solo el contenedor web publica puerto | `docker-compose.yml` |

Dos detalles que merecen mención en una defensa:

- **El login responde igual** para un correo inexistente y para una contraseña incorrecta. Si
  respondiera distinto, cualquiera podría averiguar qué correos están registrados.
- **El token del WebSocket viaja en la cadena de consulta** porque el navegador no permite
  fijar cabeceras en el *handshake*. Se traslada al flujo normal de validación en
  `OnMessageReceived` ([Program.cs:87](../backend/src/SWMatrc.Api/Program.cs#L87)).

**Roles:**

| Rol | Puede |
|---|---|
| Consulta | Ver tablero e historial |
| Operador | + reconocer alertas, activar sensores, fijar valores |
| Administrador | + crear/editar/eliminar sensores, usuarios, bitácora, reinicio |

> **Comprobado:** rol Consulta recibe `403` al crear un sensor, ver la bitácora o reiniciar;
> sin token o con token inválido, `401`.

### 2.5 Escalabilidad ✅

El enunciado pide «agregar nuevos sensores o comunidades **sin modificar la arquitectura**».

| Eje | Cómo se resuelve | Código a tocar |
|---|---|---|
| Más sensores | Alta desde la interfaz; el sensor lleva sus umbrales | Ninguno |
| Más comunidades | `Comunidad` es la unidad de agrupación; consultas, alertas y grupos del hub ya están particionados por ella | Ninguno |
| Más fenómenos | Una clase `IReglaRiesgo` + una línea de registro | Aditivo |
| Telemetría real | Implementar `ISimuladorClima` con el adaptador del datalogger | Una clase |
| Más instancias de API | SignalR admite backplane (Redis) sin tocar la aplicación | Configuración |

### 2.6 Estética ✅

Consola de operaciones con fondo oscuro, para que **el color de la alerta sea lo único que
destaque** en pantalla. Tema claro alternativo para proyectar o imprimir, con la preferencia
recordada.

Responsive real: rejilla de dos columnas en escritorio; en móvil la navegación pasa a panel
deslizante y las tablas anchas se desplazan **dentro de su contenedor**, nunca la página.

Accesibilidad contemplada: `aria-live` en los avisos, `role="meter"` en las barras de sensor,
foco visible solo por teclado, y respeto a `prefers-reduced-motion` — el parpadeo sostenido de
una alerta puede resultar incapacitante.

---

## 3. Tecnologías

| Exigido | Usado | Nota |
|---|---|---|
| Angular 20+ | **Angular 21.0.4** | Componentes autónomos, señales, sin `zone.js` |
| C# .NET 10+ | **.NET 10.0.302 (LTS)** | TFM `net10.0` |
| SQL Server 2022+ | **SQL Server 2022** | Imagen `mssql/server:2022-latest`, edición Developer |

> **Sobre el nombre de la plataforma.** «.NET Core» dejó de existir como nombre en la versión
> 3.1. Desde .NET 5 (2020) la plataforma se llama simplemente **.NET**, y .NET 10 es la
> versión LTS actual. Los paquetes conservan «Core» por continuidad
> (`Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`), pero la plataforma no.

---

## 4. Persistencia de la información

El enunciado enumera seis conjuntos de datos. El esquema tiene **siete tablas**: las seis
pedidas más `Comunidades`, que es la que hace posible el requisito de escalabilidad.

| # | Pedido | Tabla | Configuración |
|---|---|---|---|
| 1 | Usuarios | `Usuarios` | [Configuraciones.cs:11](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L11) |
| 2 | Sensores | `Sensores` | [línea 46](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L46) |
| 3 | Lecturas de sensores | `Lecturas` | [línea 83](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L83) |
| 4 | Alertas generadas | `Alertas` | [línea 103](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L103) |
| 5 | Historial de eventos | `EventosHistorial` | [línea 148](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L148) |
| 6 | Bitácora de acciones | `Bitacora` | [línea 180](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L180) |
| — | *(soporte de escalabilidad)* | `Comunidades` | [línea 29](../backend/src/SWMatrc.Infrastructure/Persistencia/Configuraciones/Configuraciones.cs#L29) |

El esquema se crea con **migraciones de EF Core** aplicadas automáticamente al arrancar la API
([InicializadorBaseDatos.cs](../backend/src/SWMatrc.Infrastructure/Persistencia/InicializadorBaseDatos.cs)),
con reintentos porque en Docker la API suele estar lista antes que el motor de base de datos.
Si el esquema está vacío se cargan los datos iniciales; si ya tiene datos, no se toca nada.

El diagrama entidad-relación completo, los índices y las reglas de borrado están en
[arquitectura.md](arquitectura.md#4-modelo-de-datos). Tres decisiones que conviene poder
defender:

1. **Índice filtrado** sobre alertas abiertas: son una fracción mínima del total acumulado y
   se consultan en cada evaluación.
2. **Desnormalización deliberada** de la última lectura, del nombre del sensor en el historial
   y del correo en la bitácora — cada una con su motivo.
3. **Cascadas acotadas**: solo desde `Comunidades`. Las alertas son evidencia y no se borran
   por un cambio de inventario. (SQL Server además rechaza dos rutas de cascada hacia la misma
   tabla, así que la restricción técnica y la del negocio coinciden.)

### Bitácora

Registra quién, qué, cuándo y desde qué dirección IP. Se escribe **siempre desde el servidor**
y no se expone ninguna operación de modificación ni de borrado sobre ella.

Detalle no obvio: el registro del **inicio de sesión** necesita un camino aparte. Cuando se
escribe, el token acaba de emitirse y el contexto de la petición sigue siendo anónimo, así que
el autor se indica de forma explícita (`RegistrarComoAsync`). Sin eso, la auditoría atribuiría
todos los accesos a «sistema».

---

## 5. Contenedores Docker

> «La base de datos deberá ejecutarse dentro de un contenedor Docker, al igual que la
> aplicación web y la API, utilizando un servidor GNU/Linux sin interfaz gráfica.»

Tres contenedores, todos sobre imágenes Linux, sin componentes gráficos:

| Servicio | Imagen base | Tamaño | Publica |
|---|---|---|---|
| `db` | `mcr.microsoft.com/mssql/server:2022-latest` | 2,31 GB | — |
| `api` | `mcr.microsoft.com/dotnet/aspnet:10.0` | 383 MB | — |
| `web` | `nginx:1.27-alpine` | 74,5 MB | `8080` |

**Solo `web` publica un puerto.** La API y SQL Server son alcanzables únicamente desde la red
interna de Docker: el motor de base de datos nunca queda expuesto a Internet.

Ambos Dockerfile son **multi-etapa**: se compila con el SDK y se despliega solo el runtime.
Ni el SDK de .NET ni Node llegan a la imagen final. La API corre como el usuario sin
privilegios `app`, no como root.

El arranque está **ordenado por salud**, no por tiempo: `api` espera a que `db` responda de
verdad a una consulta (`condition: service_healthy`), no a que exista el proceso.

Hay una segunda superposición, [docker-compose.dev.yml](../docker-compose.dev.yml), que publica
los puertos internos para desarrollo. **No** se llama `docker-compose.override.yml` a
propósito: Compose aplicaría ese nombre automáticamente, y bastaría un despliegue descuidado
para dejar SQL Server abierto a Internet.

**Despliegue en VPS** ⬜ — El procedimiento está documentado en el
[README](../README.md#8-despliegue-en-vps), incluido el proxy inverso con TLS y el respaldo de
la base. No está desplegado. El enunciado lo pide «de preferencia» y aclara que no otorga
puntos adicionales.

---

## 6. Consideraciones de evaluación

| # | Criterio | Dónde se sustenta |
|---|---|---|
| 1 | Correcto funcionamiento | Verificado end-to-end: login, WebSocket, CRUD, roles, reinicio |
| 2 | Arquitectura de la solución | 4 capas con dependencias hacia adentro; dominio sin infraestructura |
| 3 | Uso adecuado de Angular y .NET | Señales, sin `zone.js`, carga diferida · DI, `BackgroundService`, EF Core, `IExceptionHandler` |
| 4 | Comunicación en tiempo real | WebSocket exclusivo, grupos por comunidad, reconexión con resuscripción |
| 5 | Persistencia de datos | 7 tablas, migraciones, siembra idempotente |
| 6 | Diseño de la base de datos | Índice filtrado, índice descendente, desnormalización razonada, cascadas acotadas |
| 7 | Calidad de la interfaz | Sistema de diseño con tokens, dos temas, código de colores del protocolo |
| 8 | Experiencia de usuario | Estado del canal siempre visible, acuse a un clic, confirmaciones explicativas, accesibilidad |
| 9 | Organización y calidad del código | Nomenclatura consistente en español, una responsabilidad por archivo, comentarios que explican *por qué* |
| 10 | Contenedores Docker | Multi-etapa, sin privilegios, red interna, arranque por salud |
| 11 | Buenas prácticas y documentación | 34 pruebas del dominio, README, este documento, diagramas |

**Sobre el criterio 11** — El proyecto tiene **54 pruebas** repartidas en dos suites:

| Suite | Pruebas | Cubre |
|---|---|---|
| [SWMatrc.Domain.Tests](../backend/tests/SWMatrc.Domain.Tests) | 35 | Motor de riesgo y clasificación por umbrales |
| [SWMatrc.Application.Tests](../backend/tests/SWMatrc.Application.Tests) | 19 | Ciclo de monitoreo, watchdog y salvaguardas de cuentas |

Las del dominio corren en **58 ms sin base de datos ni servidor**. Eso no es casualidad: es
la consecuencia directa de que el dominio no dependa de la infraestructura. La prueba que más
valor tiene es la más aburrida — `EnCalma_noEmiteNingunaAlerta` — porque un sistema que avisa
en un día tranquilo deja de ser creíble y se acaba ignorando.

Los dobles de prueba están escritos a mano, sin biblioteca de simulación: son pocos y
simples, y así las pruebas no dependen de nada más que de xUnit.

```bash
cd backend && dotnet test
# Domain.Tests       Superado: 35   Duración:  58 ms
# Application.Tests  Superado: 19   Duración: 665 ms
```

La [integración continua](../.github/workflows/ci.yml) ejecuta en cada empujón: compilación
del backend **con las advertencias tratadas como errores**, las 54 pruebas, la compilación del
frontend y la construcción de las dos imágenes de Docker.

---

## 7. Estado real: qué falta

Este apartado existe para que el documento sea útil y no publicitario.

### Resuelto desde la primera revisión

| Pendiente | Cómo se cerró |
|---|---|
| Pantalla de gestión de usuarios | Alta, edición de rol, restablecimiento de contraseña y baja lógica, con tres salvaguardas contra quedarse sin administrador |
| `SinSenal` declarado y sin asignar | Watchdog completo: el simulador falla, el ciclo lo detecta y el tablero lo muestra |
| Pruebas más allá del dominio | Suite de aplicación con 19 pruebas de ciclo, watchdog y cuentas |
| Integración continua | Flujo de trabajo que compila, prueba y construye las imágenes |

### Todavía pendiente

| # | Pendiente | Impacto | Esfuerzo |
|---|---|---|---|
| 1 | **Verificación manual en Chrome, Firefox, Edge y Safari.** Es requisito no funcional explícito y no se ha comprobado a mano. | Medio | Bajo |
| 2 | **Pruebas de extremo a extremo de la API** con `WebApplicationFactory`, y pruebas de frontend con Vitest. Hoy la cobertura llega hasta la capa de aplicación. | Bajo | Medio |
| 3 | **Despliegue en VPS con dominio.** Documentado pero no ejecutado. | Bajo — opcional y sin puntos | Medio |

## 8. Guion sugerido para la demostración

1. **Login** con la cuenta de administrador. Señalar el indicador «En vivo» de la barra: es el
   estado del WebSocket.
2. **Dashboard.** Mostrar cómo las cifras cambian solas cada tres segundos. Abrir las
   herramientas del navegador, pestaña Red, filtro WS: se ve **una sola conexión** recibiendo
   mensajes, ninguna petición repetida.
3. **Provocar una alerta.** Ir a *Sensores* → *Fijar valor* en el limnímetro → introducir
   `4.8`. Vuelve el aviso sonoro, la franja se pone roja y aparece la alerta con su mensaje.
   Explicar que 4,5 m es la cota de desbordamiento configurada en el sensor.
4. **Reconocer** la alerta y mostrar que queda registrada con nombre y hora.
5. **Historial**: el episodio ya está anotado, con fenómeno, nivel máximo y duración.
6. **Bitácora**: la acción de fijar el valor y el acuse de recibo aparecen auditados con IP.
7. **Cambiar de rol.** Salir, entrar como `consulta@swmatrc.org` y mostrar que los botones de
   administración desaparecen — y que la API responde `403` aunque se llame directamente.
8. **Cerrar** con `docker compose ps`: tres contenedores, un solo puerto publicado.
