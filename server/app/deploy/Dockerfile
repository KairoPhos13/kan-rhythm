# syntax=docker/dockerfile:1
# kan! server: single-process image (ARM64 Graviton friendly: node:22-alpine
# is multi-arch and no x86-only native modules are used).

FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json tsconfig.build.json ./
COPY src ./src
RUN npm run build

FROM node:22-alpine AS runtime
ENV NODE_ENV=production
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY --from=build /app/dist ./dist
COPY migrations ./migrations
RUN chown -R node:node /app
USER node
EXPOSE 3000
# Migrations run at start-up; the container then serves the API.
CMD ["sh", "-c", "node dist/db/migrate-cli.js && node dist/main.js"]
