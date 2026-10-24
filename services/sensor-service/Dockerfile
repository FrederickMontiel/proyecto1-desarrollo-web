# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Sensor Service (.NET 10 + SQL Server + SignalR).
#
# El contexto de construcción es services/ (no esta carpeta) porque el proyecto usa la
# librería compartida dotnet-comun/. Se restaura antes de copiar el código para que un
# cambio en un .cs no invalide la caché de NuGet.
# --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /origen

COPY Directory.Build.props ./
COPY dotnet-comun/SWMatrc.Comun/SWMatrc.Comun.csproj dotnet-comun/SWMatrc.Comun/
COPY sensor-service/src/SWMatrc.Sensores/SWMatrc.Sensores.csproj sensor-service/src/SWMatrc.Sensores/
RUN dotnet restore sensor-service/src/SWMatrc.Sensores/SWMatrc.Sensores.csproj

COPY dotnet-comun/ dotnet-comun/
COPY sensor-service/src/ sensor-service/src/
RUN dotnet publish sensor-service/src/SWMatrc.Sensores/SWMatrc.Sensores.csproj \
    --configuration Release \
    --no-restore \
    --output /publicacion

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /aplicacion

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080

# curl solo lo usa el healthcheck de Compose.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /publicacion ./

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "SWMatrc.Sensores.dll"]
