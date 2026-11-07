#!/usr/bin/env bash
# --------------------------------------------------------------------------------------
# Traslada los datos del despliegue con Docker Compose (Fase 3) al clúster de Kubernetes:
# PostgreSQL (auth_db, community_db, alert_db), SQL Server (sensor_db) y MongoDB
# (report_db, audit_db). Necesita docker (con los contenedores de Compose en marcha) y
# kubectl apuntando al clúster, en la misma máquina.
#
#   bash infrastructure/k8s/scripts/trasladar-datos.sh
#
# Detiene los microservicios del clúster mientras restaura (sus esquemas se reemplazan) y
# los vuelve a levantar al final. Las bases del clúster deben estar desplegadas.
# --------------------------------------------------------------------------------------
set -euo pipefail

NS=swmatrc
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# En Git Bash (Windows) las rutas que empiezan con "/" se convierten a rutas de Windows. Las
# que van DENTRO de los contenedores no deben tocarse: estos envoltorios lo desactivan.
dentro_docker() { MSYS_NO_PATHCONV=1 docker exec "$@"; }
dentro_k8s() { MSYS_NO_PATHCONV=1 kubectl -n "$NS" exec "$@"; }

DEPLOYMENTS=(auth-service community-service sensor-service sensor-simulador alert-service report-service audit-service)

for c in swmatrc-postgres swmatrc-sqlserver swmatrc-mongo; do
  docker inspect -f '{{.State.Running}}' "$c" 2>/dev/null | grep -q true \
    || { echo "El contenedor $c no está en marcha (docker compose up -d postgres sqlserver mongo)." >&2; exit 1; }
done

echo "==> Deteniendo los microservicios del clúster…"
for d in "${DEPLOYMENTS[@]}"; do kubectl -n "$NS" scale deployment/"$d" --replicas=0 >/dev/null; done
kubectl -n "$NS" wait --for=delete pod -l app.kubernetes.io/component=microservicio --timeout=120s 2>/dev/null || true

echo "==> PostgreSQL"
for base in auth_db community_db alert_db; do
  rol="${base%_db}_svc"
  dentro_docker swmatrc-postgres pg_dump -U postgres -Fc "$base" >"$TMP/$base.dump"
  # --role: los objetos restaurados quedan a nombre del usuario del servicio, no de postgres.
  dentro_k8s -i postgres-0 -- pg_restore -U postgres -d "$base" --clean --if-exists \
    --no-owner --no-privileges --role="$rol" <"$TMP/$base.dump"
  echo "  $base ($(du -h "$TMP/$base.dump" | cut -f1))"
done

echo "==> SQL Server"
dentro_docker swmatrc-sqlserver bash -c 'mkdir -p /var/opt/mssql/backup && /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "BACKUP DATABASE sensor_db TO DISK = N'"'"'/var/opt/mssql/backup/sensor_db.bak'"'"' WITH INIT, COPY_ONLY" -o /dev/null'
MSYS2_ARG_CONV_EXCL="swmatrc-sqlserver:" docker cp swmatrc-sqlserver:/var/opt/mssql/backup/sensor_db.bak "$TMP/sensor_db.bak"
dentro_k8s sqlserver-0 -- mkdir -p /var/opt/mssql/backup
dentro_k8s -i sqlserver-0 -- bash -c 'cat > /var/opt/mssql/backup/sensor_db.bak' <"$TMP/sensor_db.bak"
# El usuario sensor_svc de la base restaurada trae el SID del servidor de origen: se vuelve
# a enlazar con el login sensor_svc que creó el Job sqlserver-init.
dentro_k8s sqlserver-0 -- bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "
  IF DB_ID(N'"'"'sensor_db'"'"') IS NOT NULL ALTER DATABASE sensor_db SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  RESTORE DATABASE sensor_db FROM DISK = N'"'"'/var/opt/mssql/backup/sensor_db.bak'"'"' WITH REPLACE;
  ALTER DATABASE sensor_db SET MULTI_USER;
  EXEC(N'"'"'USE sensor_db; ALTER USER sensor_svc WITH LOGIN = sensor_svc;'"'"');" -o /dev/null'
echo "  sensor_db ($(du -h "$TMP/sensor_db.bak" | cut -f1))"

echo "==> MongoDB"
for base in report_db audit_db; do
  dentro_docker swmatrc-mongo sh -c "mongodump -u root -p \"\$MONGO_INITDB_ROOT_PASSWORD\" --authenticationDatabase admin --db=$base --archive --quiet" >"$TMP/$base.archive"
  dentro_k8s -i mongo-0 -- sh -c "mongorestore -u root -p \"\$MONGO_INITDB_ROOT_PASSWORD\" --authenticationDatabase admin --nsInclude='$base.*' --drop --archive --quiet" <"$TMP/$base.archive"
  echo "  $base ($(du -h "$TMP/$base.archive" | cut -f1))"
done

echo "==> Levantando los microservicios…"
for d in "${DEPLOYMENTS[@]}"; do
  replicas=2; [[ "$d" == "sensor-simulador" ]] && replicas=1
  kubectl -n "$NS" scale deployment/"$d" --replicas="$replicas" >/dev/null
done
for d in "${DEPLOYMENTS[@]}"; do kubectl -n "$NS" rollout status deployment/"$d" --timeout=600s; done
echo "Datos trasladados."
