/// <reference types="node" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],

  build: {
    outDir: '../NARS/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        main:  resolve(__dirname, 'index.html'),
        login: resolve(__dirname, 'login.html'),
      },
      // Leaflet and leaflet-draw are loaded via CDN in index.html.
      // Marking them as external tells Rollup not to bundle them,
      // and the globals map tells it what window variable to use instead.
      external: ['leaflet'],
      output: {
        globals: { leaflet: 'L' },
      },
    },
  },

  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        cookieDomainRewrite: 'localhost',
      },
    },
  },
})
