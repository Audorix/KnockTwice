# syntax=docker/dockerfile:1
# Knock Twice web image: static React build served by unprivileged nginx, which also
# forwards /api, /health and /openapi to the `api` service (same origin, no CORS).
# Build context: repository root.

FROM node:24-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
COPY web/apps/chat-web/package.json apps/chat-web/
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
COPY docker/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/apps/chat-web/dist /usr/share/nginx/html
EXPOSE 8080

# Healthy when nginx answers and passes /health/live through to the API (busybox wget is in the
# alpine image; the API image has no HTTP client, so this one check covers both).
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/health/live || exit 1
