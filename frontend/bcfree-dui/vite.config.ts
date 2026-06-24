import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// base: './' keeps the built asset paths relative, which matters because this app is served from
// a WebView2 virtual host mapping (https://bcfree.local/index.html), not from a real domain root.
export default defineConfig({
  base: './',
  plugins: [vue()],
})
