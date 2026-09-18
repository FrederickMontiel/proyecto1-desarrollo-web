#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Cortafuegos de la VPS con UFW: solo SSH, HTTP y HTTPS quedan abiertos.
#
#   sudo bash infra/firewall.sh            # SSH en el puerto 22
#   sudo PUERTO_SSH=2222 bash infra/firewall.sh
#
# Es idempotente: se puede ejecutar varias veces sin duplicar reglas.
#
# Importante: Docker publica los puertos escribiendo sus propias reglas de iptables, por
# delante de UFW. Por eso docker-compose.yml publica la web solo en 127.0.0.1 y no
# publica SQL Server: el cortafuegos no bastaría para ocultarlos.
# --------------------------------------------------------------------------------------
set -euo pipefail

PUERTO_SSH="${PUERTO_SSH:-22}"

if [[ $EUID -ne 0 ]]; then
  echo "Ejecute con sudo." >&2
  exit 1
fi

if ! command -v ufw >/dev/null; then
  apt-get update -qq && apt-get install -y -qq ufw
fi

# SSH primero: activar UFW sin esta regla dejaría la sesión actual fuera del servidor.
ufw allow "$PUERTO_SSH"/tcp comment 'SSH'
ufw limit "$PUERTO_SSH"/tcp comment 'SSH: limita intentos de fuerza bruta'

ufw allow 80/tcp comment 'HTTP (redirección a HTTPS y renovación de Let'"'"'s Encrypt)'
ufw allow 443/tcp comment 'HTTPS'

ufw default deny incoming
ufw default allow outgoing

ufw --force enable
ufw status verbose

# Comprobación: ningún contenedor de SWMATRC debe escuchar en una interfaz pública.
expuestos="$(docker ps --filter 'name=swmatrc' --format '{{.Names}} {{.Ports}}' | grep -E '0\.0\.0\.0|:::' || true)"
if [[ -n "$expuestos" ]]; then
  echo
  echo "ATENCIÓN: estos contenedores publican puertos en todas las interfaces y UFW no los protege:" >&2
  echo "$expuestos" >&2
  echo "Revise IP_PUBLICACION en .env (debe ser 127.0.0.1)." >&2
fi
