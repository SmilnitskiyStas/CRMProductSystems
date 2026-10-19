FROM node:20-alpine

WORKDIR /app

COPY package*.json ./
RUN npm install

COPY tsconfig.json ./
COPY src ./src

# Process-level check only (the worker has no HTTP port): node must still be running.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD pgrep -x node >/dev/null || exit 1

CMD ["npm", "run", "dev"]
