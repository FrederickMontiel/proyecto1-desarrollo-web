# Estación de sensores reales — Raspberry Pi

La Raspberry Pi mide temperatura, humedad (DHT11) y vibración (SW-420) y envía cada lectura al
**Sensor Service**. El sistema trata esas lecturas como cualquier otra: las evalúa en Alert
Service, guarda su nivel de alerta en el historial y las muestra en tiempo real en el dashboard.
Si la Raspberry deja de reportar más de 30 s, sus sensores pasan a **"sin señal"**.

```text
Raspberry Pi ──POST /api/sensors/iot/readings (X-Api-Key)──▶ Ingress ─▶ API Gateway ─▶ Sensor Service
                                                                          (sensor-service: guarda el valor recibido)
                         sensor-simulador (cada 3 s): valor recibido ─▶ Alert (nivel) ─▶ SQL Server ─▶ SignalR ─▶ dashboard
```

## Cableado

| Sensor | Pin del sensor | Raspberry Pi |
|---|---|---|
| DHT11 | DATA | GPIO 4 (pin 7) |
| SW-420 | DO | GPIO 17 (pin 11) |

## En el sistema

1. Los sensores deben existir en Sensor Service con estos códigos (ya creados en la comunidad
   *Estación IoT UMG*):

   | Código | Tipo | Unidad | Rango |
   |---|---|---|---|
   | `RPI-TEMP-01` | Temperatura | °C | −10 a 50 |
   | `RPI-HUM-01` | Humedad | % | 0 a 100 |
   | `RPI-VIB-01` | Otro (vibración) | % del tiempo vibrando | 0 a 100 |

2. El dispositivo y sus sensores se declaran en la configuración de Sensor Service
   (`IOT_RASPBERRY_LLAVE` e `IOT_RASPBERRY_SENSORES` en `.env`; en Kubernetes, el Secret
   `swmatrc-iot` y el ConfigMap). El simulador **no** inventa valores para esos sensores.
3. Los umbrales de alerta de cada sensor se ajustan en la pantalla de sensores (umbrales),
   igual que los demás.

## En la Raspberry

Los archivos están en `~/swmatrc/`:

```bash
# Configuración (la llave es IOT_RASPBERRY_LLAVE del servidor)
nano ~/swmatrc/sensores.env           # SWMATRC_URL y SWMATRC_LLAVE

# Prueba manual
set -a; . ~/swmatrc/sensores.env; set +a
~/sensores-env/bin/python ~/swmatrc/sensores_iot.py

# Como servicio: arranca con la Raspberry y se reinicia solo
sudo cp ~/swmatrc/swmatrc-sensores.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now swmatrc-sensores
journalctl -u swmatrc-sensores -f
```

`SWMATRC_URL` según el entorno:

| Entorno | Valor |
|---|---|
| Producción (VPS con dominio) | `https://<su-dominio>` |
| K3s en la PC de desarrollo (con `publicar-wsl.ps1` ejecutado como administrador) | `http://<IP de la PC en la red local>`, p. ej. `http://192.168.11.133` |
| Docker Compose en la PC (con `IP_PUBLICACION=0.0.0.0`) | `http://<IP de la PC>:10000` |

## Seguridad

- Cada dispositivo tiene su propia llave (64 caracteres hexadecimales) y solo puede reportar los
  sensores que tiene asignados. Una llave robada no sirve para falsear otra comunidad.
- La llave vive en `sensores.env` con permisos `600` y, en el servidor, en un Secret de
  Kubernetes que solo reciben las réplicas de la API de Sensor (el simulador no la tiene).
- Valores fuera del rango físico del sensor se rechazan y quedan en el registro.
- Para cambiar la llave: `openssl rand -hex 32`, actualizarla en `.env` y en la Raspberry, y
  ejecutar `infrastructure/k8s/scripts/crear-secretos.sh` + `kubectl -n swmatrc rollout restart deployment/sensor-service`.
