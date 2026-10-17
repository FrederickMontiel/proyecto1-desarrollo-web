# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Alert Service (Java 25 + Spring Boot 4 + PostgreSQL).
#
# Se compila y se prueba dentro de la imagen: no hace falta un JDK en la máquina de
# desarrollo. La caché de Maven se monta aparte (BuildKit) para no descargar las
# dependencias en cada construcción.
# --------------------------------------------------------------------------------------
FROM maven:3.10-eclipse-temurin-25 AS build
WORKDIR /origen

COPY pom.xml ./
RUN --mount=type=cache,target=/root/.m2 mvn -B -q dependency:go-offline

COPY src/ src/
# Las pruebas corren aquí: si una regla de riesgo se rompe, la imagen no se construye.
RUN --mount=type=cache,target=/root/.m2 mvn -B package \
    && java -Djarmode=tools -jar target/alert-service.jar extract --layers --launcher --destination /capas

# --------------------------------------------------------------------------------------
# Imagen final: solo el JRE y la aplicación por capas (dependencias aparte del código,
# así un cambio en el código no vuelve a subir 60 MB de librerías).
# --------------------------------------------------------------------------------------
FROM eclipse-temurin:25-jre-alpine AS final
WORKDIR /aplicacion

RUN addgroup -S alertas && adduser -S -G alertas -u 10001 alertas

COPY --from=build /capas/dependencies/ ./
COPY --from=build /capas/spring-boot-loader/ ./
COPY --from=build /capas/snapshot-dependencies/ ./
COPY --from=build /capas/application/ ./

USER alertas
EXPOSE 8080

# El contenedor decide la memoria: la JVM usa un porcentaje del límite, no del host.
ENV JAVA_TOOL_OPTIONS="-XX:MaxRAMPercentage=75 -XX:+ExitOnOutOfMemoryError"

HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["java", "org.springframework.boot.loader.launch.JarLauncher"]
