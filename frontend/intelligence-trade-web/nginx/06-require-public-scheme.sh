#!/bin/sh
# Fail-fast до генерации nginx config: public scheme задаёт только deployment, без угадывания по умолчанию.
set -eu

case "${PUBLIC_SCHEME:-}" in
  http | https) ;;
  *)
    echo "PUBLIC_SCHEME должен быть http или https: схема, по которой browser открывает public origin (https, если TLS завершается перед frontend)." >&2
    exit 1
    ;;
esac
