import path from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Minimal Vitest setup — proves `npm test` works end-to-end (audit Block 0).
// Full coverage across features is a separate, later task (audit Block 13).
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Mirrors the "@/*" -> "./*" path alias in tsconfig.json so feature
      // imports (e.g. `@/lib/utils`) resolve the same way inside tests.
      "@": path.resolve(__dirname, "."),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
  },
});
