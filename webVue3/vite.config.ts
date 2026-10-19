import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

// Port 4201 runs beside the Angular portal (web/, :4200) — scope change 2026-10-12, D2.
// `/api` goes to the same backend the Angular portal proxies to (web/proxy.conf.json).
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 4201,
    strictPort: true,
    proxy: { '/api': { target: 'http://localhost:5173', changeOrigin: true, secure: false } },
  },
});
