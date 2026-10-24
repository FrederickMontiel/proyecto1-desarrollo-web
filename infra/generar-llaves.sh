#!/bin/bash
# --------------------------------------------------------------------------------------
# Genera la llave privada RSA con la que el Auth Service firma los JWT (RS256).
#
#   ./infra/generar-llaves.sh
#
# La llave queda en secrets/jwt-private.pem, carpeta que no se versiona. Compose la monta
# como secreto en /run/secrets/jwt_private_key. La pública no hace falta guardarla: el
# servicio la deriva y la publica en /.well-known/jwks.json.
#
# Si la llave ya existe no se sobrescribe: rotarla invalida todas las sesiones abiertas.
# Para rotarla a propósito, borre el archivo y vuelva a ejecutar el script.
# --------------------------------------------------------------------------------------
set -euo pipefail

directorio="$(cd "$(dirname "$0")/.." && pwd)/secrets"
archivo="$directorio/jwt-private.pem"

mkdir -p "$directorio"

if [[ -f "$archivo" ]]; then
    echo "La llave ya existe en $archivo; no se modifica."
    exit 0
fi

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$archivo"
# El contenedor corre con un usuario sin privilegios que no es el dueño del archivo:
# necesita permiso de lectura para todos. En la VPS el directorio queda solo para root.
chmod 644 "$archivo"
chmod 700 "$directorio" 2>/dev/null || true

echo "Llave generada en $archivo."
