import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // The built SPA is what the server serves; the server is the whole app.
  build: { outDir: '../src/7PaceDesktop.Server/wwwroot', emptyOutDir: true },
  server: {
    port: 5173,
    // In development the API lives on the dotnet server; run it with
    // dotnet run --project src/7PaceDesktop.Server -- --Port=5111
    proxy: { '/api': 'http://127.0.0.1:5111' },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    globals: true,
  },
})
