#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Restaura la base de datos de SWMATRC desde un respaldo generado por respaldo.sh.
#
#   bash infra/restaurar.sh /var/backups/swmatrc/SwmatrcDb-20261007-023000.bak.gz
#
# REEMPLAZA la base actual. Detiene la API mientras restaura para que nadie escriba a
# mitad del proceso, y la vuelve a levantar al terminar.
# --------------------------------------------------------------------------------------
set -euo pipefail

if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "Uso: bash infra/restaurar.sh <respaldo.bak | respaldo.bak.gz>" >&2
  exit 1
fi

ORIGEN="$(realpath "$1")"
DIR_PROYECTO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
DIR_CONTENEDOR=/var/opt/mssql/data

cd "$DIR_PROYECTO"

leer_env() { grep -E "^$1=" .env | tail -n 1 | cut -d= -f2-; }
SA_PASSWORD="$(leer_env MSSQL_SA_PASSWORD)"
BASE="$(leer_env MSSQL_DATABASE)"
BASE="${BASE:-SwmatrcDb}"

read -r -p "Se REEMPLAZARÁ la base '$BASE' con $(basename "$ORIGEN"). Escriba RESTAURAR para continuar: " respuesta
if [[ "$respuesta" != "RESTAURAR" ]]; then
  echo "Cancelado."
  exit 1
fi

temporal="$(mktemp -d)"
trap 'rm -rf "$temporal"' EXIT

archivo="restauracion.bak"
if [[ "$ORIGEN" == *.gz ]]; then
  gunzip -c "$ORIGEN" > "$temporal/$archivo"
else
  cp "$ORIGEN" "$temporal/$archivo"
fi

sql() {
  docker compose exec -T -e SQLCMDPASSWORD="$SA_PASSWORD" db \
    "$SQLCMD" -S localhost -U sa -C -b -Q "$1"
}

echo "Deteniendo la API…"
docker compose stop api

docker compose cp "$temporal/$archivo" "db:$DIR_CONTENEDOR/$archivo"

echo "Restaurando…"
sql "IF DB_ID('$BASE') IS NOT NULL ALTER DATABASE [$BASE] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
     RESTORE DATABASE [$BASE] FROM DISK = N'$DIR_CONTENEDOR/$archivo' WITH REPLACE, CHECKSUM;
     ALTER DATABASE [$BASE] SET MULTI_USER;"

docker compose exec -T db rm -f "$DIR_CONTENEDOR/$archivo"

echo "Levantando la API…"
docker compose start api

echo "Restauración completada."
