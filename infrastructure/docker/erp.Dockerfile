# Build context: repository root. JAVA_VERSION must match <java.version> in erp/pom.xml.
ARG JAVA_VERSION=27
FROM maven:3-eclipse-temurin-${JAVA_VERSION} AS build
WORKDIR /src
COPY erp/pom.xml erp/pom.xml
RUN mvn -B -q -f erp/pom.xml dependency:go-offline
COPY erp/src erp/src
RUN mvn -B -q -f erp/pom.xml -DskipTests package && cp erp/target/erp-*.jar /erp.jar

FROM eclipse-temurin:${JAVA_VERSION}-jre
COPY --from=build /erp.jar /app/erp.jar
EXPOSE 8082
ENTRYPOINT ["java", "-jar", "/app/erp.jar"]
