import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  // Files in publicDir are copied as-is to outDir (wwwroot).
  // login.html, login.css and NARS.jpg must live here so emptyOutDir
  // doesn't delete them from wwwroot on each build.
  publicDir: 'public',

  plugins: [
    vue(),
  ],

  build: {
    outDir: '../NARS/wwwroot',
    emptyOutDir: true,
    // Turf + Geoman make any GIS bundle large. 800 kB is a realistic ceiling
    // for this dependency set; the split below keeps each chunk well under it.
    chunkSizeWarningLimit: 800,

    // Vite 8 uses Rolldown — manual chunking goes under rolldownOptions.
    rolldownOptions: {
      output: {
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return

          // Geoman + its large polyclip-ts polygon engine — together, always cached.
          if (id.includes('leaflet-geoman-free') || id.includes('polyclip-ts'))
            return 'vendor-geoman'

          // Leaflet core — small and very stable.
          if (id.includes('/leaflet/'))
            return 'vendor-leaflet'

          // Turf is ~500 kB minified. Keep it separate so it can be cached
          // independently, and because road-directions.ts already lazy-imports it.
          if (id.includes('@turf'))
            return 'vendor-turf'

          // Graphology (graph library for road directions) — also stable.
          if (id.includes('graphology'))
            return 'vendor-graphology'
        },
      },
    },
  },
})
