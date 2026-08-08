# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Etapa de compilación. Se restauran los paquetes antes de copiar el código para que un
# cambio en un .cs no invalide la caché de NuGet.
# --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /origen

COPY src/SWMatrc.Domain/SWMatrc.Domain.csproj           src/SWMatrc.Domain/
COPY src/SWMatrc.Application/SWMatrc.Application.csproj src/SWMatrc.Application/
COPY src/SWMatrc.Infrastructure/SWMatrc.Infrastructure.csproj src/SWMatrc.Infrastructure/
COPY src/SWMatrc.Api/SWMatrc.Api.csproj                 src/SWMatrc.Api/

# Se restaura desde el proyecto de la API, no desde la solución: arrastra las tres
# referencias transitivas y deja fuera el proyecto de pruebas, que no se despliega.
RUN dotnet restore src/SWMatrc.Api/SWMatrc.Api.csproj

COPY src/ src/

RUN dotnet publish src/SWMatrc.Api/SWMatrc.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /publicacion

# --------------------------------------------------------------------------------------
# Imagen final. Solo el runtime de ASP.NET: ni SDK ni código fuente llegan a producción.
# --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /aplicacion

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

# curl es lo único que se añade sobre la imagen base: lo usa el healthcheck de Compose
# para comprobar /health desde dentro del contenedor.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /publicacion ./

# La imagen base trae el usuario sin privilegios 'app' (UID 1654). El proceso no necesita
# escribir en el sistema de archivos, así que no hay razón para dejarlo como root.
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "SWMatrc.Api.dll"]
