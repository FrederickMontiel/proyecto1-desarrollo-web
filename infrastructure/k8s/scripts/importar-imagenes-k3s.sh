#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Importa al containerd de K3s las imágenes exportadas por construir-imagenes.sh. Root.
#
#   wsl -d Ubuntu-24.04 -u root bash infrastructure/k8s/scripts/importar-imagenes-k3s.sh
#
# Después basta reiniciar los Deployments para que tomen la imagen nueva:
#   kubectl -n swmatrc rollout restart deployment
# --------------------------------------------------------------------------------------
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Ejecute este script como root (sudo)." >&2
  exit 1
fi

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ETIQUETA="${ETIQUETA:-fase4}"
archivo="$DIR/.imagenes/swmatrc-$ETIQUETA.tar"

if [[ ! -f "$archivo" ]]; then
  echo "No existe $archivo. Ejecute antes construir-imagenes.sh." >&2
  exit 1
fi

echo "==> Importando $archivo en K3s…"
k3s ctr --namespace k8s.io images import "$archivo"
k3s ctr --namespace k8s.io images ls -q | grep "swmatrc/" | grep ":$ETIQUETA"
