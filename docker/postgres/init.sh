#!/bin/sh
# Cria os papéis da aplicação: "app" (sem CREATEDB, dono do Catálogo) e "provisioner" (CREATEDB, só para criar bancos de tenant).
set -eu

psql -v ON_ERROR_STOP=1 --username postgres <<SQL
CREATE ROLE subscribely_app LOGIN PASSWORD '${APP_DB_PASSWORD}';
CREATE ROLE subscribely_provisioner LOGIN CREATEDB PASSWORD '${PROVISIONER_DB_PASSWORD}';
-- O provisioner cria bancos com OWNER subscribely_app.
GRANT subscribely_app TO subscribely_provisioner;
CREATE DATABASE subscribely_catalog OWNER subscribely_app;
SQL
