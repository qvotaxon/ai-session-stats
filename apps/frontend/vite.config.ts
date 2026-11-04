import { readFileSync } from "node:fs";
import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const BACKEND = "http://127.0.0.1:5180";

/** Read lazily per request so the dev server works when the backend (and its token file) starts later. */
const readToken = (): string | undefined => {
  const base = process.env.LOCALAPPDATA;
  if (!base) return undefined;
  try {
    return readFileSync(path.join(base, "SessionInsights", "token"), "utf8").trim();
  } catch {
    return undefined;
  }
};

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: process.env.SI_API_URL ?? BACKEND,
        changeOrigin: true,
        configure: (proxy) => {
          proxy.on("proxyReq", (proxyReq) => {
            const token = readToken();
            if (token) proxyReq.setHeader("X-Session-Insights-Token", token);
          });
        },
      },
    },
  },
  preview: {
    port: 4173,
    strictPort: true,
  },
});
