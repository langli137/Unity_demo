// 登录页（A1）：用户名/密码 → POST /api/v1/auth/login → 存会话回调父级。

import { useState, type FormEvent } from 'react'
import { http } from '../api/client'
import { saveSession, type LoginResponse } from '../api/auth'

interface Props {
  onSuccess: () => void
}

export function LoginPage({ onSuccess }: Props) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (pending) return
    setPending(true)
    setError(null)
    try {
      const resp = await http.post<LoginResponse>('/api/v1/auth/login', {
        username: username.trim(),
        password,
      })
      saveSession(resp)
      onSuccess()
    } catch (err) {
      setError(err instanceof Error ? err.message : '登录失败')
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="card login-card">
      <h1>园区巡检系统</h1>
      <p className="login-sub">请登录后管理巡检问题</p>
      <form className="edit-form" onSubmit={handleSubmit}>
        <label className="form-field form-field-wide">
          <span>用户名</span>
          <input
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            placeholder="admin 或 inspector"
            autoComplete="username"
            required
          />
        </label>
        <label className="form-field form-field-wide">
          <span>密码</span>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="登录密码"
            autoComplete="current-password"
            required
          />
        </label>
        <div className="form-actions form-field-wide">
          <button type="submit" className="btn-primary" disabled={pending}>
            {pending ? '登录中…' : '登 录'}
          </button>
        </div>
        {error && <p className="error form-field-wide">{error}</p>}
      </form>
      <p className="login-hint">
        演示账号：admin / admin123（管理员，可删除）；inspector / inspector123（巡检员）
      </p>
    </section>
  )
}
