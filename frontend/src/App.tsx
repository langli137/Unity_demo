// 轻量视图切换：两个页面用不上路由库（禁止引入未要求的框架），
// 用受控的 view 状态切换列表/详情即可。
// A1：外层加登录门控——无会话/401 过期时只渲染 LoginPage。

import { useEffect, useState } from 'react'
import { IssueListPage } from './pages/IssueListPage'
import { IssueDetailPage } from './pages/IssueDetailPage'
import { LoginPage } from './pages/LoginPage'
import { AUTH_EXPIRED_EVENT, clearSession, getStoredUser, getToken } from './api/auth'

type View = { name: 'list' } | { name: 'detail'; id: string }

export default function App() {
  const [view, setView] = useState<View>({ name: 'list' })
  // 会话状态以 token 存在性为准；登录/过期时翻转。
  const [authed, setAuthed] = useState(() => !!getToken())

  useEffect(() => {
    const onExpired = () => {
      setAuthed(false)
      setView({ name: 'list' }) // 过期回列表根，重新登录后不落在旧详情上
    }
    window.addEventListener(AUTH_EXPIRED_EVENT, onExpired)
    return () => window.removeEventListener(AUTH_EXPIRED_EVENT, onExpired)
  }, [])

  if (!authed) {
    return (
      <main className="container">
        <LoginPage onSuccess={() => setAuthed(true)} />
      </main>
    )
  }

  const user = getStoredUser()

  const logout = () => {
    clearSession()
    setAuthed(false)
    setView({ name: 'list' })
  }

  return (
    <main className="container">
      <div className="topbar">
        <span className="topbar-user">
          {user ? `${user.displayName}（${user.role === 'admin' ? '管理员' : '巡检员'}）` : ''}
        </span>
        <button onClick={logout}>退出登录</button>
      </div>
      {view.name === 'list' ? (
        <IssueListPage onOpenDetail={(id) => setView({ name: 'detail', id })} />
      ) : (
        <IssueDetailPage id={view.id} onBack={() => setView({ name: 'list' })} />
      )}
    </main>
  )
}
