#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Despliega SWMATRC en Kubernetes/K3s.
#
#   bash infrastructure/k8s/scripts/desplegar.sh local
#   DOMINIO=monitoreo.midominio.org CORREO_ACME=yo@midominio.org \
#     bash infrastructure/k8s/scripts/desplegar.sh produccion
#   DOMINIO=… bash infrastructure/k8s/scripts/desplegar.sh produccion-proxy   # servidor compartido
#
# 1. Secrets y ConfigMaps de inicialización (crear-secretos.sh).
# 2. kubectl apply del overlay (con el dominio real sustituido en producción).
# 3. Espera a que las bases, los microservicios, el gateway y la web estén listos.
# --------------------------------------------------------------------------------------
set -euo pipefail

ENTORNO="${1:-local}"
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NS=swmatrc

case "$ENTORNO" in
  local | produccion | produccion-proxy) ;;
  *) echo "Uso: $0 local|produccion|produccion-proxy" >&2; exit 1 ;;
esac

bash "$DIR/scripts/crear-secretos.sh"

echo "==> Aplicando overlays/$ENTORNO"
manifiestos="$(kubectl kustomize "$DIR/overlays/$ENTORNO")"
if [[ "$ENTORNO" != "local" ]]; then
  [[ -n "${DOMINIO:-}" ]] || { echo "Defina DOMINIO=su-dominio para producción." >&2; exit 1; }
  manifiestos="${manifiestos//sistema-climatico.example/$DOMINIO}"
  manifiestos="${manifiestos//admin@$DOMINIO/${CORREO_ACME:-admin@$DOMINIO}}"
  # IMAGENES_LOCALES=1: usar las imágenes construidas en el propio servidor
  # (construir-imagenes.sh + importar-imagenes-k3s.sh) en lugar de las de ghcr.io.
  if [[ "${IMAGENES_LOCALES:-0}" == "1" ]]; then
    ETIQUETA="${ETIQUETA:-fase4}"
    manifiestos="$(printf '%s\n' "$manifiestos" \
      | sed -E "s#ghcr\.io/[a-z0-9_.-]+/swmatrc-([a-z-]+):latest#swmatrc/\1:$ETIQUETA#g" \
      | sed -E "s#imagePullPolicy: Always#imagePullPolicy: IfNotPresent#g")"
  fi
fi
printf '%s\n' "$manifiestos" | kubectl apply -f -

echo "==> Esperando a las bases de datos…"
for sts in postgres sqlserver mongo; do
  kubectl -n "$NS" rollout status statefulset/"$sts" --timeout=600s
done
kubectl -n "$NS" wait --for=condition=complete job/sqlserver-init --timeout=600s 2>/dev/null || true

echo "==> Esperando a los microservicios…"
for d in redis auth-service community-service sensor-service sensor-simulador alert-service \
         report-service audit-service api-gateway web; do
  kubectl -n "$NS" rollout status deployment/"$d" --timeout=600s
done

echo
kubectl -n "$NS" get pods -o wide
echo
kubectl -n "$NS" get ingress
if [[ "$ENTORNO" == "local" ]]; then
  echo
  echo "Listo: http://localhost"
else
  echo
  echo "Listo: https://$DOMINIO  (el certificado puede tardar 1–2 minutos: kubectl -n $NS get certificate)"
fi
