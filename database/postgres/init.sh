#!/bin/bash
# --------------------------------------------------------------------------------------
# Primer arranque de PostgreSQL: una base y un usuario por microservicio.
#
# Cada servicio solo conoce las credenciales de su propia base y no tiene permisos sobre
# las demás, así que la regla "nadie toca la base de otro" la hace cumplir el motor, no la
# buena voluntad del código.
#
# La imagen oficial ejecuta este script únicamente cuando el volumen está vacío. Para
# aplicar un cambio en un volumen existente hay que crear el usuario a mano o recrearlo.
# --------------------------------------------------------------------------------------
set -euo pipefail

crear_base() {
    local base="$1" usuario="$2" password="$3"

    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
        -v base="$base" -v usuario="$usuario" -v password="$password" <<'SQL'
CREATE ROLE :"usuario" LOGIN PASSWORD :'password';
CREATE DATABASE :"base" OWNER :"usuario" ENCODING 'UTF8' TEMPLATE template0;
REVOKE ALL ON DATABASE :"base" FROM PUBLIC;
SQL

    echo "Base $base creada para el usuario $usuario."
}

crear_base auth_db      auth_svc      "$AUTH_DB_PASSWORD"
crear_base community_db community_svc "$COMMUNITY_DB_PASSWORD"
crear_base alert_db     alert_svc     "$ALERT_DB_PASSWORD"
