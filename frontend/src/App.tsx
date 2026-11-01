// 轻量视图切换：两个页面用不上路由库（禁止引入未要求的框架），
// 用受控的 view 状态切换列表/详情即可。

import { useState } from 'react'
import { IssueListPage } from './pages/IssueListPage'
import { IssueDetailPage } from './pages/IssueDetailPage'

type View = { name: 'list' } | { name: 'detail'; id: string }

export default function App() {
  const [view, setView] = useState<View>({ name: 'list' })

  return (
    <main className="container">
      {view.name === 'list' ? (
        <IssueListPage onOpenDetail={(id) => setView({ name: 'detail', id })} />
      ) : (
        <IssueDetailPage id={view.id} onBack={() => setView({ name: 'list' })} />
      )}
    </main>
  )
}
