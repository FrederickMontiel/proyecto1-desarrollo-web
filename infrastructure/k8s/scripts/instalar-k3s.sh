#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Instala K3s (Kubernetes ligero) en Linux: WSL Ubuntu o la VPS. Requiere root.
#
#   WSL (desde Windows):   wsl -d Ubuntu-24.04 -u root bash infrastructure/k8s/scripts/instalar-k3s.sh
#   VPS:                   sudo bash infrastructure/k8s/scripts/instalar-k3s.sh
#
# Variables opcionales:
#   CERT_MANAGER=1          instala cert-manager (necesario en producción para HTTPS)
#   CERT_MANAGER_VERSION    versión de cert-manager (por omisión v1.18.2)
#   K3S_CHANNEL             canal de K3s (por omisión stable)
#   TRAEFIK_PUERTO_HTTP     puerto donde publicar Traefik en lugar del 80  (p. ej. 8081)
#   TRAEFIK_PUERTO_HTTPS    puerto donde publicar Traefik en lugar del 443 (p. ej. 8444)
#
# SERVIDOR COMPARTIDO: si otro proxy (nginx, Caddy…) ya atiende 80/443, defina los dos
# puertos alternos. La configuración de Traefik se deja ANTES de instalar K3s, así Traefik
# nunca toma el 80/443 (ni por un instante) y no le roba el tráfico a los otros sitios.
# Ese proxy existente reenvía el dominio del sistema a 127.0.0.1:$TRAEFIK_PUERTO_HTTP
# (ver infrastructure/nginx/nginx-k3s.conf.example y el overlay produccion-proxy).
#
# K3s trae: containerd, Traefik (Ingress Controller), CoreDNS, metrics-server (para el
# HPA), local-path-provisioner (PersistentVolumes) y controlador de NetworkPolicies.
# --------------------------------------------------------------------------------------
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Ejecute este script como root (sudo)." >&2
  exit 1
fi

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CANAL="${K3S_CHANNEL:-stable}"

# Configuración de Traefik colocada en la carpeta de manifiestos de K3s: K3s la aplica al
# arrancar, antes de desplegar Traefik.
MANIFIESTOS=/var/lib/rancher/k3s/server/manifests
mkdir -p "$MANIFIESTOS"
if [[ -n "${TRAEFIK_PUERTO_HTTP:-}" || -n "${TRAEFIK_PUERTO_HTTPS:-}" ]]; then
  : "${TRAEFIK_PUERTO_HTTP:?Defina también TRAEFIK_PUERTO_HTTP}" "${TRAEFIK_PUERTO_HTTPS:?Defina también TRAEFIK_PUERTO_HTTPS}"
  echo "==> Traefik se publicará en $TRAEFIK_PUERTO_HTTP/$TRAEFIK_PUERTO_HTTPS (80/443 quedan libres para el proxy existente)"
  cat >"$MANIFIESTOS/traefik-swmatrc.yaml" <<EOF
apiVersion: helm.cattle.io/v1
kind: HelmChartConfig
metadata:
  name: traefik
  namespace: kube-system
spec:
  valuesContent: |-
    additionalArguments:
      - "--entryPoints.web.transport.respondingTimeouts.readTimeout=0s"
      - "--entryPoints.websecure.transport.respondingTimeouts.readTimeout=0s"
      # El proxy de delante (en este mismo servidor) manda X-Forwarded-For/Proto.
      - "--entryPoints.web.forwardedHeaders.trustedIPs=127.0.0.1/32,10.42.0.0/16"
    ports:
      web:
        exposedPort: $TRAEFIK_PUERTO_HTTP
      websecure:
        exposedPort: $TRAEFIK_PUERTO_HTTPS
EOF
else
  cp "$DIR/k3s/traefik-config.yaml" "$MANIFIESTOS/traefik-swmatrc.yaml"
fi

if ! command -v k3s >/dev/null 2>&1; then
  echo "==> Instalando K3s (canal $CANAL)…"
  # --write-kubeconfig-mode 644: kubectl funciona sin sudo para el usuario normal.
  curl -sfL https://get.k3s.io | INSTALL_K3S_CHANNEL="$CANAL" INSTALL_K3S_EXEC="--write-kubeconfig-mode 644" sh -
else
  echo "==> K3s ya está instalado: $(k3s --version | head -n1)"
fi

echo "==> Esperando a que el nodo esté listo…"
until kubectl get nodes 2>/dev/null | grep -q " Ready"; do sleep 3; done
kubectl get nodes -o wide

echo "==> Servicio de Traefik:"
until kubectl -n kube-system get svc traefik >/dev/null 2>&1; do sleep 3; done
kubectl -n kube-system get svc traefik

if [[ "${CERT_MANAGER:-0}" == "1" ]]; then
  VERSION="${CERT_MANAGER_VERSION:-v1.18.2}"
  echo "==> Instalando cert-manager $VERSION…"
  kubectl apply -f "https://github.com/cert-manager/cert-manager/releases/download/$VERSION/cert-manager.yaml"
  kubectl -n cert-manager rollout status deployment/cert-manager-webhook --timeout=180s
fi

# Copia el kubeconfig para el usuario que invocó sudo (o el usuario por omisión de WSL).
USUARIO="${SUDO_USER:-$(getent passwd 1000 | cut -d: -f1)}"
if [[ -n "$USUARIO" ]]; then
  CASA="$(getent passwd "$USUARIO" | cut -d: -f6)"
  install -d -o "$USUARIO" -g "$USUARIO" -m 700 "$CASA/.kube"
  install -o "$USUARIO" -g "$USUARIO" -m 600 /etc/rancher/k3s/k3s.yaml "$CASA/.kube/config"
  echo "==> kubeconfig copiado a $CASA/.kube/config"
fi

echo
echo "K3s listo. Siguiente paso: importar las imágenes y desplegar (ver infrastructure/k8s/README.md)."
