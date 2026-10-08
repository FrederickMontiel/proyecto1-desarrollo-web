#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Respaldo completo de la base de datos de SWMATRC.
#
# Genera un .bak con CHECKSUM dentro del contenedor de SQL Server, lo verifica, lo copia a
# la VPS comprimido y borra los respaldos más antiguos que la retención configurada.
#
#   bash infra/respaldo.sh
#
# Variables opcionales:
#   DESTINO_RESPALDOS  carpeta en la VPS            (por omisión /var/backups/swmatrc)
#   RETENCION_DIAS     días que se conservan        (por omisión 14)
#
# Para programarlo, ver infra/cron.example.
# --------------------------------------------------------------------------------------
set -euo pipefail

DIR_PROYECTO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DESTINO="${DESTINO_RESPALDOS:-/var/backups/swmatrc}"
RETENCION_DIAS="${RETENCION_DIAS:-14}"
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
DIR_CONTENEDOR=/var/opt/mssql/data

cd "$DIR_PROYECTO"

# Se leen solo las dos variables necesarias en lugar de ejecutar el .env completo.
leer_env() { grep -E "^$1=" .env | tail -n 1 | cut -d= -f2-; }
SA_PASSWORD="$(leer_env MSSQL_SA_PASSWORD)"
BASE="$(leer_env MSSQL_DATABASE)"
BASE="${BASE:-SwmatrcDb}"

if [[ -z "$SA_PASSWORD" ]]; then
  echo "No se encontró MSSQL_SA_PASSWORD en $DIR_PROYECTO/.env" >&2
  exit 1
fi

marca="$(date +%Y%m%d-%H%M%S)"
archivo="${BASE}-${marca}.bak"

# La contraseña viaja en SQLCMDPASSWORD y no como argumento, para que no aparezca en la
# lista de procesos del contenedor.
sql() {
  docker compose exec -T -e SQLCMDPASSWORD="$SA_PASSWORD" db \
    "$SQLCMD" -S localhost -U sa -C -b -Q "$1"
}

echo "[$(date -Is)] Respaldando $BASE…"

# COPY_ONLY: no altera la cadena de respaldos si más adelante se adoptan diferenciales.
sql "BACKUP DATABASE [$BASE] TO DISK = N'$DIR_CONTENEDOR/$archivo' WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT"
sql "RESTORE VERIFYONLY FROM DISK = N'$DIR_CONTENEDOR/$archivo' WITH CHECKSUM"

mkdir -p "$DESTINO"
chmod 700 "$DESTINO"

docker compose cp "db:$DIR_CONTENEDOR/$archivo" "$DESTINO/$archivo"
docker compose exec -T db rm -f "$DIR_CONTENEDOR/$archivo"

gzip -f "$DESTINO/$archivo"
chmod 600 "$DESTINO/$archivo.gz"

borrados="$(find "$DESTINO" -name "${BASE}-*.bak.gz" -type f -mtime +"$RETENCION_DIAS" -print -delete | wc -l)"

echo "[$(date -Is)] Listo: $DESTINO/$archivo.gz ($(du -h "$DESTINO/$archivo.gz" | cut -f1)). Respaldos antiguos eliminados: $borrados."
