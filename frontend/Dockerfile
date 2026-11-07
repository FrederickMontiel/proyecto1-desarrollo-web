# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Etapa de compilación. Se instalan las dependencias antes de copiar el código fuente
# para que un cambio en un componente no obligue a repetir el npm ci.
# --------------------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /origen

COPY package.json package-lock.json ./

# npm ci respeta el lockfile al pie de la letra: la imagen se compila con exactamente las
# mismas versiones que en desarrollo.
RUN npm ci

COPY . .

RUN npm run build

# --------------------------------------------------------------------------------------
# Imagen final: nginx sirviendo los archivos estáticos. Node no llega a producción.
# --------------------------------------------------------------------------------------
FROM nginx:1.27-alpine AS final

# Plantilla: el entrypoint de nginx la procesa con envsubst y deja el resultado en conf.d.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template

# NGINX_ENTRYPOINT_LOCAL_RESOLVERS hace que el entrypoint lea los DNS de /etc/resolv.conf.
# GATEWAY_URL: "api-gateway" en Compose; en Kubernetes, el nombre completo del Service.
ENV NGINX_ENTRYPOINT_LOCAL_RESOLVERS=1     GATEWAY_URL=http://api-gateway:8080
COPY --from=build /origen/dist/swmatrc-web/browser /usr/share/nginx/html

EXPOSE 80

# Se sondea 127.0.0.1 y no localhost: nginx escucha solo en IPv4, mientras que dentro del
# contenedor localhost resuelve primero a ::1 y la comprobación fallaría siempre.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
