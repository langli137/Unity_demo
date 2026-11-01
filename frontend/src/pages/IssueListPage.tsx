// 问题列表页：筛选 + 分页 + 表格 + 10 秒轮询（轮询在 useIssues 内）。
// v1.2：新增"新建问题"补录入口（AR 上报为主，管理端可补录漏报问题）。

import { useState, type FormEvent } from 'react'
import { FilterBar } from '../components/FilterBar'
import { IssueTable } from '../components/IssueTable'
import { IssueForm, type IssueFormValue } from '../components/IssueForm'
import { useIssues } from '../hooks/useIssues'
import { useCreateIssue } from '../hooks/useCreateIssue'

const PAGE_SIZE = 20

const EMPTY_DRAFT: IssueFormValue = {
  title: '',
  description: '',
  category: 'other',
  priority: 'medium',
  reporterName: '',
}

export function IssueListPage({ onOpenDetail }: { onOpenDetail: (id: string) => void }) {
  const { items, total, loading, error, filters, updateFilters, setPage, refresh } =
    useIssues()
  const { create, pending: creating, error: createError, clearError } = useCreateIssue()
  const [creatingOpen, setCreatingOpen] = useState(false)
  const [draft, setDraft] = useState<IssueFormValue>(EMPTY_DRAFT)

  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  const openCreate = () => {
    clearError()
    setDraft(EMPTY_DRAFT)
    setCreatingOpen(true)
  }

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    // 管理端补录无现场 AR 坐标：position 置零占位（anchor 由后端缺省），上报人留空用"管理端补录"标注。
    const created = await create({
      title: draft.title,
      description: draft.description,
      category: draft.category,
      priority: draft.priority,
      reporterName: draft.reporterName.trim() === '' ? '管理端补录' : draft.reporterName,
      position: { x: 0, y: 0, z: 0 },
    })
    if (created) {
      setCreatingOpen(false)
      setDraft(EMPTY_DRAFT)
      await refresh()
      // 创建成功后直接跳详情，方便补录者继续完善。
      onOpenDetail(created.id)
    }
    // 失败留在表单内，错误由 createError 透出。
  }

  return (
    <section className="card">
      <header className="page-header">
        <h1>巡检问题列表</h1>
        <button className="btn-primary" onClick={openCreate}>＋ 新建问题</button>
        <button onClick={() => void refresh()}>刷新</button>
      </header>

      <FilterBar
        status={filters.status}
        category={filters.category}
        onStatusChange={(status) => updateFilters({ status })}
        onCategoryChange={(category) => updateFilters({ category })}
      />

      {creatingOpen && (
        <div className="create-panel">
          <h2>新建问题（管理端补录）</h2>
          <IssueForm
            value={draft}
            onChange={setDraft}
            onSubmit={(e) => void handleCreate(e)}
            onCancel={() => {
              clearError()
              setCreatingOpen(false)
            }}
            pending={creating}
            error={createError}
            submitLabel="创建"
          />
        </div>
      )}

      {error && <p className="error">加载失败：{error}</p>}
      {loading && <p className="loading">加载中…</p>}

      <IssueTable items={items} onRowClick={onOpenDetail} />

      <footer className="pagination">
        <button
          disabled={filters.page <= 1}
          onClick={() => setPage(filters.page - 1)}
        >
          上一页
        </button>
        <span>
          第 {filters.page} / {pageCount} 页（共 {total} 条）
        </span>
        <button
          disabled={filters.page >= pageCount}
          onClick={() => setPage(filters.page + 1)}
        >
          下一页
        </button>
      </footer>
    </section>
  )
}
