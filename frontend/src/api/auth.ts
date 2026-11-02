// A1 登录态：类型 + localStorage 持久化 + 变更通知（单一来源）。

export type Role = 'admin' | 'inspector'

export interface User {
  username: string
  displayName: string
  role: Role
}

export interface LoginResponse {
  token: string
  user: User
}

const TOKEN_KEY = 'park_inspect_token'
const USER_KEY = 'park_inspect_user'

// 401 时 App 需要回到登录页：client 派发事件，App 监听。
export const AUTH_EXPIRED_EVENT = 'park-inspect:auth-expired'

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function getStoredUser(): User | null {
  const raw = localStorage.getItem(USER_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as User
  } catch {
    return null
  }
}

export function saveSession(resp: LoginResponse): void {
  localStorage.setItem(TOKEN_KEY, resp.token)
  localStorage.setItem(USER_KEY, JSON.stringify(resp.user))
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(USER_KEY)
}

export function notifyAuthExpired(): void {
  clearSession()
  window.dispatchEvent(new Event(AUTH_EXPIRED_EVENT))
}

export function isAdmin(user: User | null): boolean {
  return user?.role === 'admin'
}
