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

    poolOptions: {
      forks: {
        maxForks: 4,
        minForks: 1,
      },
    },

    resolve: {
      alias: {
        "@": path.resolve(__dirname, "src"),
      },
    },

    plugins,

    build: {
      outDir: "dist",
      emptyOutDir: true,
      // The Geoman editor is loaded lazily via a dynamic import in map-init.ts
      // on the first draw/edit session. It must NOT be assigned to a manual
      // chunk — doing so pulls it into the eager entry graph and forces the
      // ~460KB bundle to be fetched (and executed) at page load. Leaving it on
      // automatic code-splitting keeps it as a true lazy chunk that is fetched
      // on demand only when the user first opens a draw or edit session.
      // Source maps only for non-production builds — shipping maps publicly
      // exposes the full original TS/Vue source. Keep them for dev/staging.
      sourcemap: mode !== "production",
      // Maplibre GL is ~1MB and is the always-needed baseline, so don't treat
      // it (or the on-demand ~460KB geoman chunk) as an oversized-chunk error.
      chunkSizeWarningLimit: 1100,
      rollupOptions: {
        output: {
          entryFileNames: "assets/[name]-[hash].js",
          chunkFileNames: "assets/[name]-[hash].js",
          assetFileNames: "assets/[name]-[hash].[ext]",
          manualChunks(id: string) {
            if (!id.includes("node_modules")) return

            // Maplibre GL JS - the core map library (always needed)
            if (id.includes("maplibre-gl")) return "vendor-maplibre"

            // Graphology - road network graph (changes rarely)
            if (id.includes("graphology")) return "vendor-graphology"

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
      pool: "forks",
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
