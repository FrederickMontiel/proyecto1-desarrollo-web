#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Crea (o actualiza) los Secrets de Kubernetes y los ConfigMaps de inicialización de las
# bases a partir de archivos locales que NO se versionan (RNF-OAUTH-01/02):
#
#   .env                     contraseñas de bases, secretos de servicio, admin, OAuth
#   secrets/jwt-private.pem  llave RSA con que Auth Service firma los JWT
#
#   bash infrastructure/k8s/scripts/crear-secretos.sh
#   ARCHIVO_ENV=/ruta/produccion.env bash infrastructure/k8s/scripts/crear-secretos.sh
#
# Secrets resultantes en el namespace swmatrc:
#   swmatrc-db         contraseñas de PostgreSQL, SQL Server y MongoDB (una por servicio)
#   swmatrc-jwt        jwt-private.pem
#   swmatrc-servicios  secretos de servicio a servicio y CLIENTES_SERVICIO de Auth
#   swmatrc-admin      administrador inicial (solo se usa con la base vacía)
#   swmatrc-oauth      Client ID / Client Secret de Google y GitHub
#   swmatrc-iot        llave con que la Raspberry Pi envía sus lecturas
# --------------------------------------------------------------------------------------
set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
ENV_FILE="${ARCHIVO_ENV:-$RAIZ/.env}"
LLAVE="${LLAVE_JWT:-$RAIZ/secrets/jwt-private.pem}"
NS=swmatrc

[[ -f "$ENV_FILE" ]] || { echo "No existe $ENV_FILE (copie .env.example)." >&2; exit 1; }
[[ -f "$LLAVE" ]] || { echo "No existe $LLAVE (ejecute infrastructure/docker/generar-llaves.sh)." >&2; exit 1; }

# Lee una variable del .env sin ejecutarlo (los valores pueden tener símbolos).
leer() { grep -E "^$1=" "$ENV_FILE" | tail -n 1 | cut -d= -f2- | tr -d '\r' || true; }
requerida() {
  local v; v="$(leer "$1")"
  [[ -n "$v" ]] || { echo "Falta $1 en $ENV_FILE." >&2; exit 1; }
  printf '%s' "$v"
}

# Los valores van en un archivo temporal privado y no en la línea de comandos: así no
# quedan visibles en la lista de procesos.
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
chmod 700 "$TMP"

aplicar_secreto() {
  local nombre="$1" archivo="$2"
  kubectl -n "$NS" create secret generic "$nombre" --from-env-file="$archivo" \
    --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  echo "  secret/$nombre"
}

kubectl get namespace "$NS" >/dev/null 2>&1 || kubectl create namespace "$NS"
echo "==> Secrets en el namespace $NS"

cat >"$TMP/db.env" <<EOF
POSTGRES_PASSWORD=$(requerida POSTGRES_PASSWORD)
AUTH_DB_PASSWORD=$(requerida AUTH_DB_PASSWORD)
COMMUNITY_DB_PASSWORD=$(requerida COMMUNITY_DB_PASSWORD)
ALERT_DB_PASSWORD=$(requerida ALERT_DB_PASSWORD)
MSSQL_SA_PASSWORD=$(requerida MSSQL_SA_PASSWORD)
SENSOR_DB_PASSWORD=$(requerida SENSOR_DB_PASSWORD)
MONGO_ROOT_PASSWORD=$(requerida MONGO_ROOT_PASSWORD)
REPORT_DB_PASSWORD=$(requerida REPORT_DB_PASSWORD)
AUDIT_DB_PASSWORD=$(requerida AUDIT_DB_PASSWORD)
EOF
aplicar_secreto swmatrc-db "$TMP/db.env"

C="$(requerida SVC_SECRETO_COMMUNITY)"; S="$(requerida SVC_SECRETO_SENSOR)"; A="$(requerida SVC_SECRETO_ALERT)"
R="$(requerida SVC_SECRETO_REPORT)"; U="$(requerida SVC_SECRETO_AUDIT)"
cat >"$TMP/servicios.env" <<EOF
SVC_SECRETO_COMMUNITY=$C
SVC_SECRETO_SENSOR=$S
SVC_SECRETO_ALERT=$A
SVC_SECRETO_REPORT=$R
SVC_SECRETO_AUDIT=$U
CLIENTES_SERVICIO=community-service:$C,sensor-service:$S,alert-service:$A,report-service:$R,audit-service:$U
EOF
aplicar_secreto swmatrc-servicios "$TMP/servicios.env"

cat >"$TMP/admin.env" <<EOF
ADMIN_EMAIL=$(leer ADMIN_EMAIL)
ADMIN_PASSWORD=$(leer ADMIN_PASSWORD)
EOF
aplicar_secreto swmatrc-admin "$TMP/admin.env"

# Un proveedor sin credenciales queda deshabilitado (no aparece su botón).
cat >"$TMP/oauth.env" <<EOF
OAUTH_GOOGLE_CLIENT_ID=$(leer OAUTH_GOOGLE_CLIENT_ID)
OAUTH_GOOGLE_CLIENT_SECRET=$(leer OAUTH_GOOGLE_CLIENT_SECRET)
OAUTH_GITHUB_CLIENT_ID=$(leer OAUTH_GITHUB_CLIENT_ID)
OAUTH_GITHUB_CLIENT_SECRET=$(leer OAUTH_GITHUB_CLIENT_SECRET)
EOF
aplicar_secreto swmatrc-oauth "$TMP/oauth.env"
for p in GOOGLE GITHUB; do
  if [[ -n "$(leer "OAUTH_${p}_CLIENT_ID")" && -n "$(leer "OAUTH_${p}_CLIENT_SECRET")" ]]; then
    echo "    · ${p}: configurado"
  else
    echo "    · ${p}: sin credenciales (deshabilitado)"
  fi
done

# Llave del dispositivo IoT (Raspberry Pi). Solo la reciben las réplicas de la API de Sensor.
printf 'IOT_RASPBERRY_LLAVE=%s\n' "$(leer IOT_RASPBERRY_LLAVE)" >"$TMP/iot.env"
aplicar_secreto swmatrc-iot "$TMP/iot.env"

kubectl -n "$NS" create secret generic swmatrc-jwt --from-file=jwt-private.pem="$LLAVE" \
  --dry-run=client -o yaml | kubectl apply -f - >/dev/null
echo "  secret/swmatrc-jwt"

echo "==> ConfigMaps de inicialización de las bases (database/)"
crear_cm() {
  kubectl -n "$NS" create configmap "$1" --from-file="$2" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  echo "  configmap/$1"
}
crear_cm db-init-postgres "$RAIZ/database/postgres/init.sh"
crear_cm db-init-sqlserver "$RAIZ/database/sqlserver/init.sql"
crear_cm db-init-mongodb "$RAIZ/database/mongodb/init.js"
