#!/bin/sh
set -eu
: "${ConnectionStrings__Crm:?ConnectionStrings__Crm must be injected from Secrets Manager}"
exec /app/efbundle --connection "$ConnectionStrings__Crm" "$@"
