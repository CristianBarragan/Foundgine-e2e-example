# Build context: repository root. JAVA_VERSION must match the orchestration module's compiler level (CI uses 21).
ARG JAVA_VERSION=21
FROM maven:3-eclipse-temurin-${JAVA_VERSION} AS build
WORKDIR /src
COPY temporal/workflows/pom.xml temporal/workflows/pom.xml
RUN mvn -B -q -f temporal/workflows/pom.xml dependency:go-offline
COPY temporal/workflows/src temporal/workflows/src
RUN mvn -B -q -f temporal/workflows/pom.xml -DskipTests package \
 && mvn -B -q -f temporal/workflows/pom.xml dependency:copy-dependencies -DoutputDirectory=target/lib \
 && mkdir /app && cp temporal/workflows/target/orchestration-*.jar /app/orchestration.jar && cp -r temporal/workflows/target/lib /app/lib

FROM eclipse-temurin:${JAVA_VERSION}-jre
COPY --from=build /app /app
ENTRYPOINT ["java", "-cp", "/app/orchestration.jar:/app/lib/*", "com.enterprise.orchestration.OrchestrationWorker"]
