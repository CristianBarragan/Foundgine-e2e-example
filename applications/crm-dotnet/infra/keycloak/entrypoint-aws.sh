#!/bin/sh
set -eu
: "${PUBLIC_BASE_URL:?PUBLIC_BASE_URL must be set to the externally reachable CRM origin}"
sed -i "s|__PUBLIC_BASE_URL__|${PUBLIC_BASE_URL}|g" /opt/keycloak/data/import/foundgine-crm-realm.json
exec /opt/keycloak/bin/kc.sh start --import-realm --health-enabled=true
