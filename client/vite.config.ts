import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';
import gleam from 'vite-gleam';

const backendPaths = ['/api', '/login', '/logout', '/signin-oidc'];

const proxy = Object.fromEntries(
  backendPaths.map(path => [path, {
    target: process.env.BACKEND_URL ?? 'http://localhost:5000',
    changeOrigin: true,
  }]),
);

export default defineConfig({
  clearScreen: false,
  server: {
    watch: {
      ignored: [
        'build/',
      ],
    },
    proxy,
  },
  plugins: [
    gleam(),
    tailwindcss(),
  ],
});
