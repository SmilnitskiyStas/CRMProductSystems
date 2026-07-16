FROM node:22-alpine AS deps
WORKDIR /app
# TASK-377/378: next-intl pulls a nested @swc/core whose peerDependency
# @swc/helpers>=0.5.17 isn't satisfied by the hoisted @swc/helpers@0.5.5 next
# depends on. npm 10.x (bundled by node:20-alpine and node:22-alpine alike)
# treats that as an out-of-sync lockfile and hard-fails `npm ci` with EUSAGE;
# npm 11+ doesn't. Same root cause fixed in .github/workflows/ci.yml.
RUN npm install -g npm@11
COPY package*.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
ARG NEXT_PUBLIC_API_URL=http://localhost:5000
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public

EXPOSE 3100
ENV PORT=3100
CMD ["node", "server.js"]
