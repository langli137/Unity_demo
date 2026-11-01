import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 开发期把 /api 代理到本机 Go 后端（默认端口见 backend/config：PORT=8083），
// 避免直连跨域；生产构建请用 VITE_API_BASE_URL 指定后端地址。
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:8083',
    },
  },
})
