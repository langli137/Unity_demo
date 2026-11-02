// 问题列表页：筛选 + 分页 + 表格 + 10 秒轮询（轮询在 useIssues 内）。
// v1.2：新增"新建问题"补录入口（AR 上报为主，管理端可补录漏报问题）。
// v1.3：B7 新建表单接智能优先级建议；B8 导出巡检报表 CSV（按状态/分类/优先级/时间）。

import { useState, type FormEvent } from 'react'
import { FilterBar } from '../components/FilterBar'
import { IssueTable } from '../components/IssueTable'
import { IssueForm, type IssueFormValue } from '../components/IssueForm'
import { useIssues } from '../hooks/useIssues'
import { useCreateIssue } from '../hooks/useCreateIssue'
import { usePrioritySuggestion } from '../hooks/usePrioritySuggestion'
import { exportCSV } from '../api/issues'

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
  const {
    suggestion,
    suggesting,
    error: suggestError,
    fetchSuggestion,
    clear: clearSuggestion,
  } = usePrioritySuggestion()
  const [creatingOpen, setCreatingOpen] = useState(false)
  const [draft, setDraft] = useState<IssueFormValue>(EMPTY_DRAFT)
  // B8 导出面板：优先级 + 时间范围（状态/分类复用当前筛选）。
  const [exportOpen, setExportOpen] = useState(false)
  const [exportPriority, setExportPriority] = useState('')
  const [exportFrom, setExportFrom] = useState('')
  const [exportTo, setExportTo] = useState('')
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  const openCreate = () => {
    clearError()
    clearSuggestion()
    setDraft(EMPTY_DRAFT)
    setExportError(null)
    setCreatingOpen(true)
  }

  // B7 新建表单的智能建议入口。
  const handleSuggest = async () => {
    await fetchSuggestion(draft.title, draft.description)
  }

  // B8 导出 CSV：状态/分类沿用当前筛选，优先级与时间范围取面板值。
  const handleExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      const blob = await exportCSV({
        status: filters.status || undefined,
        category: filters.category || undefined,
        priority: exportPriority || undefined,
        from: exportFrom || undefined,
        to: exportTo || undefined,
      })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `巡检报表_${new Date().toISOString().slice(0, 10)}.csv`
      a.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      setExportError(err instanceof Error ? err.message : '导出失败')
    } finally {
      setExporting(false)
    }
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
        <button onClick={() => setExportOpen((v) => !v)}>导出报表</button>
        <button onClick={() => void refresh()}>刷新</button>
      </header>

      {exportOpen && (
        <div className="export-panel">
          <h2>导出巡检报表（CSV）</h2>
          <p className="export-hint">
            状态与分类沿用上方筛选；可再限定优先级与上报时间范围。
          </p>
          <div className="export-fields">
            <label>
              <span>优先级</span>
              <select
                value={exportPriority}
                onChange={(e) => setExportPriority(e.target.value)}
              >
                <option value="">全部</option>
                <option value="high">高</option>
                <option value="medium">中</option>
                <option value="low">低</option>
              </select>
            </label>
            <label>
              <span>起始日期</span>
              <input
                type="date"
                value={exportFrom}
                onChange={(e) => setExportFrom(e.target.value)}
              />
            </label>
            <label>
              <span>截止日期</span>
              <input
                type="date"
                value={exportTo}
                onChange={(e) => setExportTo(e.target.value)}
              />
            </label>
            <button
              className="btn-primary"
              disabled={exporting}
              onClick={() => void handleExport()}
            >
              {exporting ? '导出中…' : '下载 CSV'}
            </button>
            <button onClick={() => { setExportOpen(false); setExportError(null) }}>
              取消
            </button>
          </div>
          {exportError && <p className="error">{exportError}</p>}
        </div>
      )}

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
              clearSuggestion()
              setCreatingOpen(false)
            }}
            pending={creating}
            error={createError}
            submitLabel="创建"
            suggestion={suggestion}
            suggesting={suggesting}
            suggestError={suggestError}
            onSuggest={() => void handleSuggest()}
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
