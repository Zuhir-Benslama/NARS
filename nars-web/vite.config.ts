import { defineConfig, loadEnv } from "vite"
import vue from "@vitejs/plugin-vue"
import { fileURLToPath } from "node:url"
import path from "node:path"

const __dirname = path.dirname(fileURLToPath(import.meta.url))

export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, __dirname, "")
  const devBackend = env.VITE_DEV_BACKEND || "http://localhost:5000"

  const plugins = [
    vue(),
    // Meta CSP in index.html is for production; in Vite dev it often triggers
    // console violations (eval/HMR) and can block tooling. Production CSP must
    // come from the reverse proxy / ASP.NET host instead.
    ...(command === "serve"
      ? [
          {
            name: "nars-strip-csp-meta-dev",
            transformIndexHtml(html: string) {
              return html.replace(
                /<meta\s+http-equiv="Content-Security-Policy"\s+content="[^"]*"\s*\/?>/i,
                "<!-- CSP meta omitted in Vite dev; use server headers in production -->",
              )
            },
          },
        ]
      : []),
  ]

  return {
    publicDir: "public",

    resolve: {
      alias: {
        "@": path.resolve(__dirname, "src"),
      },
    },

    plugins,

    build: {
      outDir: "dist",
      emptyOutDir: true,
      // Source maps only for non-production builds — shipping maps publicly
      // exposes the full original TS/Vue source. Keep them for dev/staging.
      sourcemap: mode !== "production",
      // Maplibre GL JS is ~1MB - separated into its own chunk
      chunkSizeWarningLimit: 1500,
      rollupOptions: {
        output: {
          entryFileNames: "assets/[name]-[hash].js",
          chunkFileNames: "assets/[name]-[hash].js",
          assetFileNames: "assets/[name]-[hash].[ext]",
          manualChunks(id: string) {
            if (!id.includes("node_modules")) return

            // Maplibre GL JS - core library
            if (id.includes("maplibre-gl")) return "vendor-maplibre"

            // Turf.js - GIS operations (submodules only)
            if (id.includes("@turf")) return "vendor-turf"

            // Vue i18n - locale files
            if (id.includes("vue-i18n")) return "vendor-i18n"

            // Vue core + runtime (inlined into main chunk — needed everywhere)
            // HTML export libs (lazy-loaded, kept separate)
            // DOMPurify - sanitization
            if (id.includes("dompurify")) return "vendor-sanitize"
          },
        },
      },
    },

    server: {
      proxy: {
        // Same-origin API + login while the UI is served from port 5173.
        "/api": {
          target: devBackend,
          changeOrigin: true,
          secure: false,
        },
        "^/login$": {
          target: devBackend,
          changeOrigin: true,
          secure: false,
        },
      },
    },

    test: {
      globals: true,
      environment: "jsdom",
      setupFiles: "./src/test/setup.ts",
      include: ["**/*.{test,spec}.{js,mjs,cjs,ts,mts,cts,jsx,tsx}"],
      exclude: ["e2e/**", "node_modules/**"],
      coverage: {
        provider: "v8",
        reporter: ["text", "json", "html"],
        include: ["src/**/*.ts", "src/**/*.vue"],
        exclude: ["src/**/*.d.ts", "src/test/**"],
        thresholds: {
          statements: 60,
          branches: 52,
          functions: 62,
          lines: 61,
        },
      },
    },
  }
})
