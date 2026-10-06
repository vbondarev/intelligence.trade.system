#!/bin/sh
# Fail-fast до генерации nginx config: без корректного BFF upstream frontend service не стартует.
set -eu

upstream="${BFF_UPSTREAM:-}"
address="${upstream#*://}"

case "$upstream" in
  http://?* | https://?*) ;;
  *)
    echo "BFF_UPSTREAM должен быть абсолютным http(s) адресом BFF, например http://bff:8080." >&2
    exit 1
    ;;
esac

case "$address" in
  */* | *[!A-Za-z0-9.:_-]*)
    echo "BFF_UPSTREAM указывается как scheme://host[:port] без path и служебных символов: BFF получает исходный path запроса." >&2
    exit 1
    ;;
esac
