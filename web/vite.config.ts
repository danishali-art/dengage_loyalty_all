import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import tailwindcss from '@tailwindcss/vite';

// `/api` goes to the admin Api on :5173 (the Api project's `http` launch profile).
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 4201,
    strictPort: true,
    proxy: { '/api': { target: 'http://localhost:5173', changeOrigin: true, secure: false } },
  },
});
