FROM quay.io/keycloak/keycloak:26.3
COPY realm-foundgine-crm.aws.json /opt/keycloak/data/import/foundgine-crm-realm.json
COPY entrypoint-aws.sh /opt/keycloak/bin/entrypoint-aws.sh
USER root
RUN chmod 755 /opt/keycloak/bin/entrypoint-aws.sh
USER 1000
ENTRYPOINT ["/opt/keycloak/bin/entrypoint-aws.sh"]
