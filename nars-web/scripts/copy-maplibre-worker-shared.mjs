// MapLibre GL v6 emits the map worker under /assets as a raw copy (Vite's
// `new URL(..., import.meta.url)` handling copies the file without bundling).
// That worker statically imports ./maplibre-gl-shared.mjs at runtime, so the
// chunk must exist next to it or the worker dies ("WebGL context was lost").
// The shared chunk is a self-contained side-effect module (no imports), so a
// plain copy is sufficient. Failing loudly here (not at page load) on version
// bumps that rename or split the chunk keeps the failure at build time.
import { cpSync, mkdirSync, existsSync, readFileSync } from "node:fs"
import { dirname, resolve } from "node:path"
import { fileURLToPath } from "node:url"

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..")
const src = resolve(root, "node_modules/maplibre-gl/dist/maplibre-gl-shared.mjs")
const outDir = resolve(root, "dist/assets")

if (!existsSync(src)) {
  throw new Error(`[copy-maplibre-worker-shared] missing ${src} — maplibre-gl layout changed; update this script`)
}

mkdirSync(outDir, { recursive: true })
const out = resolve(outDir, "maplibre-gl-shared.mjs")
cpSync(src, out)
console.log(`[copy-maplibre-worker-shared] ${readFileSync(out, "utf8").length} bytes -> ${out}`)