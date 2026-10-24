# syntax=docker/dockerfile:1

# --------------------------------------------------------------------------------------
# Auth Service (NestJS). Las dependencias se instalan antes de copiar el código para que
# un cambio en un .ts no obligue a repetir el npm ci.
# --------------------------------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /origen

COPY package.json package-lock.json ./
RUN npm ci

COPY tsconfig.json tsconfig.build.json nest-cli.json ./
COPY src/ src/
RUN npm run build \
    # Solo las dependencias de producción pasan a la imagen final.
    && npm prune --omit=dev

# --------------------------------------------------------------------------------------
# Imagen final: Node, las dependencias de producción y el JavaScript compilado.
# --------------------------------------------------------------------------------------
FROM node:24-alpine AS final
WORKDIR /aplicacion

ENV NODE_ENV=production \
    PORT=3000

COPY --from=build --chown=node:node /origen/node_modules ./node_modules
COPY --from=build --chown=node:node /origen/dist ./dist
COPY --chown=node:node package.json ./

# La imagen base trae el usuario sin privilegios "node".
USER node
EXPOSE 3000

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:3000/health/live || exit 1

# Se lanza node directamente, no "npm start": así el proceso recibe el SIGTERM de Docker
# y Kubernetes y puede cerrar ordenadamente.
CMD ["node", "dist/main.js"]
