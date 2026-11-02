// fetch 统一封装：只负责传输与解包 {code,message,data}，失败抛 ApiError。
// 页面与组件不得绕过本模块直接发请求。
// A1：自动携带 Authorization: Bearer <token>；401 时通知 App 回登录页。

import type { ApiResponse } from '../types/issue'
import { getToken, notifyAuthExpired } from './auth'

// 后端统一错误：code 为响应壳中的业务码（与 HTTP 状态码一致），message 为后端文案。
export class ApiError extends Error {
  readonly code: number

  constructor(code: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.code = code
  }
}

// baseURL 读环境变量；开发期为空串走 vite 代理（见 vite.config.ts）。
export const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

function authHeaders(): Record<string, string> {
  const token = getToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(BASE_URL + path, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...authHeaders(), ...init?.headers },
    })
  } catch {
    // 网络层失败（断网/CORS/后端未启动）。
    throw new ApiError(0, '网络请求失败，请检查服务是否可用')
  }

  // 401：令牌缺失/过期——清会话并广播，App 切回登录页。
  if (res.status === 401) {
    notifyAuthExpired()
    throw new ApiError(401, '登录已过期，请重新登录')
  }

  let body: ApiResponse<T>
  try {
    body = (await res.json()) as ApiResponse<T>
  } catch {
    throw new ApiError(res.status, '响应不是合法 JSON')
  }

  // 契约：code==0 成功；失败时 code 与 HTTP 状态码一致。
  if (body.code !== 0) {
    throw new ApiError(body.code, body.message)
  }
  return body.data as T
}

export const http = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, data: unknown) =>
    request<T>(path, { method: 'POST', body: JSON.stringify(data) }),
  patch: <T>(path: string, data: unknown) =>
    request<T>(path, { method: 'PATCH', body: JSON.stringify(data) }),
  del: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
  // 二进制下载（B8 CSV / B6 图片）：绕过 JSON 解包，返回原始 Response。
  raw: (path: string, init?: RequestInit) =>
    fetch(BASE_URL + path, {
      ...init,
      headers: { ...authHeaders(), ...init?.headers },
    }),
}
