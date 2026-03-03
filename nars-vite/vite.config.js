import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],

  // Multi-page app: map (index.html) + login
  build: {
    // Output directly into the ASP.NET Core wwwroot/ folder so that
    // npm run build is all you need - no manual copy step.
    // Adjust the relative path if your folder layout differs.
    outDir: '../NARS/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        main:  resolve(__dirname, 'index.html'),
        login: resolve(__dirname, 'login.html'),
      },
    },
  },

  // Dev server: proxy /api/* to the ASP.NET Core backend
  // All fetch('/api/...') calls are forwarded to localhost:5000 during dev.
  // In production the backend serves everything on the same origin.
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})
