#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Construye las 8 imágenes del sistema con Docker y las exporta a un .tar que K3s importa.
# Se ejecuta donde esté Docker (Windows con Git Bash, o Linux):
#
#   bash infrastructure/k8s/scripts/construir-imagenes.sh            # etiqueta "fase4"
#   ETIQUETA=v4.0.1 bash infrastructure/k8s/scripts/construir-imagenes.sh
#
# Resultado: infrastructure/k8s/.imagenes/swmatrc-<etiqueta>.tar (no se versiona).
# Luego, en el nodo K3s:  sudo bash infrastructure/k8s/scripts/importar-imagenes-k3s.sh
#
# En producción no hace falta: GitHub Actions publica las imágenes en ghcr.io.
# --------------------------------------------------------------------------------------
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ETIQUETA="${ETIQUETA:-fase4}"
SALIDA="$RAIZ/infrastructure/k8s/.imagenes"
mkdir -p "$SALIDA"

# nombre | contexto | Dockerfile (relativo al contexto)
IMAGENES=(
  "auth-service|services/auth-service|Dockerfile"
  "community-service|services|community-service/Dockerfile"
  "sensor-service|services|sensor-service/Dockerfile"
  "alert-service|services/alert-service|Dockerfile"
  "report-service|services/report-service|Dockerfile"
  "audit-service|services/audit-service|Dockerfile"
  "api-gateway|services/api-gateway|Dockerfile"
  "web|frontend|Dockerfile"
)

etiquetas=()
for entrada in "${IMAGENES[@]}"; do
  IFS='|' read -r nombre contexto dockerfile <<<"$entrada"
  imagen="swmatrc/$nombre:$ETIQUETA"
  echo "==> $imagen"
  docker build -t "$imagen" -f "$RAIZ/$contexto/$dockerfile" "$RAIZ/$contexto"
  etiquetas+=("$imagen")
done

archivo="$SALIDA/swmatrc-$ETIQUETA.tar"
echo "==> Exportando a $archivo"
docker save -o "$archivo" "${etiquetas[@]}"
ls -lh "$archivo"
