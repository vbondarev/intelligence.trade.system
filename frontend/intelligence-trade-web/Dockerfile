# Build context — frontend/intelligence-trade-web: image собирается без доступа к backend.
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30-alpine AS final
# envsubst подставляет в template только BFF_UPSTREAM, не затрагивая переменные nginx.
ENV NGINX_ENVSUBST_FILTER=^BFF_UPSTREAM$
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=0755 nginx/05-require-bff-upstream.sh /docker-entrypoint.d/05-require-bff-upstream.sh
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --start-period=5s --retries=10 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/index.html || exit 1
