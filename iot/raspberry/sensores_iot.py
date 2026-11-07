#!/usr/bin/env python3
"""
Estación de sensores reales para SWMATRC — Raspberry Pi 4.

Lee los sensores conectados y envía cada medición al Sensor Service a través del API
Gateway (POST /api/sensors/iot/readings, cabecera X-Api-Key). El sistema procesa esas
lecturas igual que las simuladas: evaluación de riesgo en Alert Service, nivel de alerta,
historial y difusión en tiempo real al dashboard.

Sensores (mismo cableado que sensores.py):
  DHT11  DATA → GPIO 4  (pin 7)    temperatura (°C) y humedad relativa (%)
  SW-420 DO   → GPIO 17 (pin 11)   porcentaje del tiempo con vibración (%)

Configuración por variables de entorno (ver sensores.env.example):
  SWMATRC_URL        p. ej. https://sistema-climatico.example  o  http://localhost:18080
  SWMATRC_LLAVE      llave del dispositivo (IOT_RASPBERRY_LLAVE del servidor)
  SENSOR_TEMPERATURA código del sensor de temperatura en el sistema (RPI-TEMP-01)
  SENSOR_HUMEDAD     código del sensor de humedad (RPI-HUM-01)
  SENSOR_VIBRACION   código del sensor de vibración (RPI-VIB-01)
  INTERVALO_S        segundos entre envíos (por omisión 5; el DHT11 no admite menos de 1)
"""

import logging
import os
import signal
import sys
import threading
import time

import requests

# ---------- Configuración ----------
URL = os.environ.get("SWMATRC_URL", "http://localhost:18080").rstrip("/") + "/api/sensors/iot/readings"
LLAVE = os.environ.get("SWMATRC_LLAVE", "")
CODIGO_TEMPERATURA = os.environ.get("SENSOR_TEMPERATURA", "RPI-TEMP-01")
CODIGO_HUMEDAD = os.environ.get("SENSOR_HUMEDAD", "RPI-HUM-01")
CODIGO_VIBRACION = os.environ.get("SENSOR_VIBRACION", "RPI-VIB-01")
INTERVALO_S = max(2.0, float(os.environ.get("INTERVALO_S", "5")))
MUESTREO_S = 0.002  # Revisa el SW-420 cada 2 ms

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s", datefmt="%H:%M:%S")
log = logging.getLogger("sensores")

if not LLAVE:
    sys.exit("Falta SWMATRC_LLAVE (la llave del dispositivo).")

# Se importan después de validar la configuración: fallan si no se corre en la Raspberry.
import adafruit_dht  # noqa: E402
import board  # noqa: E402
from gpiozero import DigitalInputDevice  # noqa: E402

# ---------- Sensores ----------
dht = adafruit_dht.DHT11(board.D4)
vibracion = DigitalInputDevice(17, pull_up=False)

# ---------- Medición de vibración en segundo plano ----------
candado = threading.Lock()
muestras_total = 0
muestras_activas = 0
pulsos = 0
detener = threading.Event()


def al_vibrar():
    global pulsos
    with candado:
        pulsos += 1


vibracion.when_activated = al_vibrar


def muestreador():
    global muestras_total, muestras_activas
    while not detener.is_set():
        activo = vibracion.is_active
        with candado:
            muestras_total += 1
            if activo:
                muestras_activas += 1
        time.sleep(MUESTREO_S)


threading.Thread(target=muestreador, daemon=True).start()


def leer_vibracion():
    """(porcentaje del tiempo vibrando, pulsos) desde la última lectura."""
    global muestras_total, muestras_activas, pulsos
    with candado:
        total, activas, p = muestras_total, muestras_activas, pulsos
        muestras_total = muestras_activas = pulsos = 0
    porcentaje = round(activas / total * 100) if total else 0
    if p > 0 and porcentaje == 0:
        porcentaje = 1  # Hubo pulsos demasiado cortos para el muestreo
    return porcentaje, p


def leer_dht11(intentos=3):
    """(temperatura, humedad). El DHT11 falla seguido, así que reintenta."""
    for _ in range(intentos):
        try:
            t = dht.temperature
            h = dht.humidity
            if t is not None and h is not None:
                return float(t), float(h)
        except RuntimeError:
            pass
        time.sleep(1)
    return None, None


# ---------- Envío ----------
sesion = requests.Session()
sesion.headers.update({"X-Api-Key": LLAVE, "Content-Type": "application/json", "User-Agent": "swmatrc-raspberry/1.0"})


def enviar(lecturas):
    """
    Manda el lote. Si el servidor no responde, la medición se descarta: el sistema guarda la
    hora en que llega cada valor, así que reenviar uno viejo lo fecharía mal. Si el silencio
    se prolonga, el dashboard muestra el sensor "sin señal", que es lo correcto.
    """
    try:
        r = sesion.post(URL, json={"lecturas": lecturas}, timeout=5)
    except requests.RequestException as error:
        log.warning("Sin conexión con el sistema: %s", error.__class__.__name__)
        return
    if r.status_code == 202:
        rechazadas = r.json().get("rechazadas", [])
        for rechazo in rechazadas:
            log.warning("Rechazada %s: %s", rechazo.get("codigo"), rechazo.get("motivo"))
    elif r.status_code == 401:
        log.error("La llave del dispositivo no es válida (401). Revise SWMATRC_LLAVE.")
    else:
        log.warning("El sistema respondió %s: %s", r.status_code, r.text[:200])


def apagar(*_):
    detener.set()


signal.signal(signal.SIGTERM, apagar)
signal.signal(signal.SIGINT, apagar)

log.info("Enviando a %s cada %.0f s (Ctrl+C para salir).", URL, INTERVALO_S)

try:
    while not detener.is_set():
        inicio = time.monotonic()
        temperatura, humedad = leer_dht11()
        porcentaje, num_pulsos = leer_vibracion()

        lecturas = [{"codigo": CODIGO_VIBRACION, "valor": porcentaje}]
        if temperatura is not None:
            lecturas.append({"codigo": CODIGO_TEMPERATURA, "valor": temperatura})
            lecturas.append({"codigo": CODIGO_HUMEDAD, "valor": humedad})

        log.info(
            "Temp: %s · Hum: %s · Vibración: %s %% (%s pulsos)",
            f"{temperatura} °C" if temperatura is not None else "error de lectura",
            f"{humedad} %" if humedad is not None else "error de lectura",
            porcentaje,
            num_pulsos,
        )
        enviar(lecturas)

        detener.wait(max(0.0, INTERVALO_S - (time.monotonic() - inicio)))
finally:
    dht.exit()
    log.info("Programa detenido.")
