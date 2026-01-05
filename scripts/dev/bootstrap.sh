#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DB_NAME="${DB_NAME:-ai-trading-db}"
POSTGRES_FORMULA="${POSTGRES_FORMULA:-postgresql@16}"
REDIS_FORMULA="${REDIS_FORMULA:-redis}"
PG_DEFAULT_DB="${PG_DEFAULT_DB:-postgres}"
PG_USER="${PG_USER:-}"
INSTALL_MODE=""

if command -v brew >/dev/null 2>&1; then
  INSTALL_MODE="brew"
  if ! brew list --formula "$POSTGRES_FORMULA" >/dev/null 2>&1; then
    brew install "$POSTGRES_FORMULA"
  fi

  if ! brew list --formula "$REDIS_FORMULA" >/dev/null 2>&1; then
    brew install "$REDIS_FORMULA"
  fi

  brew services start "$POSTGRES_FORMULA" >/dev/null
  brew services start "$REDIS_FORMULA" >/dev/null

  PG_PREFIX="$(brew --prefix "$POSTGRES_FORMULA")"
  PSQL="$PG_PREFIX/bin/psql"
  CREATEDB="$PG_PREFIX/bin/createdb"
elif command -v apt-get >/dev/null 2>&1; then
  INSTALL_MODE="apt"
  SUDO=""
  if [[ "${EUID}" -ne 0 ]]; then
    if command -v sudo >/dev/null 2>&1; then
      SUDO="sudo"
    else
      echo "sudo is required to install packages on Linux." >&2
      exit 1
    fi
  fi

  $SUDO apt-get update -y
  $SUDO apt-get install -y postgresql redis-server

  if command -v systemctl >/dev/null 2>&1; then
    $SUDO systemctl enable --now postgresql >/dev/null 2>&1 || true
    $SUDO systemctl enable --now redis-server >/dev/null 2>&1 || true
  elif command -v service >/dev/null 2>&1; then
    $SUDO service postgresql start >/dev/null 2>&1 || true
    $SUDO service redis-server start >/dev/null 2>&1 || true
  fi

  if [[ -z "$PG_USER" ]]; then
    PG_USER="postgres"
  fi

  PSQL="$(command -v psql)"
  CREATEDB="$(command -v createdb)"
else
  echo "Unsupported environment. Install Postgres and Redis manually, or use Homebrew/apt-get." >&2
  exit 1
fi

if [[ ! -x "$PSQL" || ! -x "$CREATEDB" ]]; then
  echo "Postgres binaries not found. Ensure psql and createdb are available." >&2
  exit 1
fi

PSQL_ARGS=(-d "$PG_DEFAULT_DB")
CREATEDB_ARGS=()
if [[ -n "$PG_USER" ]]; then
  PSQL_ARGS+=(-U "$PG_USER")
  CREATEDB_ARGS+=(-U "$PG_USER")
fi

for _ in {1..10}; do
  if "$PSQL" "${PSQL_ARGS[@]}" -tAc "select 1" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

DB_EXISTS="$($PSQL "${PSQL_ARGS[@]}" -tAc "select 1 from pg_database where datname='${DB_NAME}';" | tr -d '[:space:]')"
if [[ "$DB_EXISTS" != "1" ]]; then
  "$CREATEDB" "${CREATEDB_ARGS[@]}" "$DB_NAME"
fi

"$PSQL" "$DB_NAME" -f "$SCRIPT_DIR/../db/init.sql" >/dev/null
