# Imagen del frontend: build Vite (con typecheck tsc -b) servido por nginx,
# que además hace de proxy inverso de /api hacia el backend (same-origin, sin CORS).
# Build: docker build -t ragapp/frontend:local .

FROM node:22-alpine AS build
WORKDIR /app

# dependencias primero: capa cacheable mientras no cambie package-lock
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine
# el nginx.conf es una PLANTILLA: el entrypoint oficial ejecuta envsubst al arrancar
# (sustituye ${APP_ACCESS_KEY}; las variables nativas $nginx no se tocan)
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

# garantizar definida para que envsubst nunca deje la variable sin sustituir
ENV APP_ACCESS_KEY=

EXPOSE 80
