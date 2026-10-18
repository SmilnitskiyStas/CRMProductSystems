import { resolve } from 'node:path';
import { defineConfig, externalizeDepsPlugin } from 'electron-vite';
import react from '@vitejs/plugin-react';

// Production loads index.html from file:// where response headers do not apply, so the CSP is
// injected as a <meta> at build time. In dev the main process sets a relaxed header instead
// (Vite's React refresh preamble is an inline script).
const PROD_CSP =
  "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
  "connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'";

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    resolve: { alias: { '@shared': resolve('src/shared') } },
    build: { rollupOptions: { input: { index: resolve('electron/main.ts') } } },
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    resolve: { alias: { '@shared': resolve('src/shared') } },
    build: { rollupOptions: { input: { index: resolve('electron/preload.ts') } } },
  },
  renderer: {
    root: '.',
    plugins: [
      react(),
      {
        name: 'prod-csp-meta',
        apply: 'build',
        transformIndexHtml: (html: string) =>
          html.replace(
            '<meta charset="UTF-8" />',
            `<meta charset="UTF-8" />\n    <meta http-equiv="Content-Security-Policy" content="${PROD_CSP}" />`,
          ),
      },
    ],
    resolve: { alias: { '@shared': resolve('src/shared'), '@': resolve('src') } },
    build: { rollupOptions: { input: { index: resolve('index.html') } } },
  },
});
