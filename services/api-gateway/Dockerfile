# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# API Gateway (ASP.NET Core 10 + YARP). Único punto de entrada a los microservicios.
# Se restaura antes de copiar el código para que un cambio en un .cs no invalide la caché.
# --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /origen

COPY SWMatrc.Gateway/SWMatrc.Gateway.csproj SWMatrc.Gateway/
RUN dotnet restore SWMatrc.Gateway/SWMatrc.Gateway.csproj

COPY SWMatrc.Gateway/ SWMatrc.Gateway/
RUN dotnet publish SWMatrc.Gateway/SWMatrc.Gateway.csproj \
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

ENTRYPOINT ["dotnet", "SWMatrc.Gateway.dll"]
