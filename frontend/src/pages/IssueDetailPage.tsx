// 问题详情页：完整字段展示 + 状态操作 + 字段编辑（v1.2 用户要求"增加编辑功能"）。
// 三态任意互转合法（v1.1 Q1）；409 一定提示并刷新。
// 编辑范围 = 标题/描述/分类/优先级/上报人；状态走上方状态按钮，AR 定位字段只读
//（由 AR 上报时一次性写入，管理端不改）。表单复用 components/IssueForm。
// v1.3：A4 删除（admin）+ B6 现场照片查看。

import { useEffect, useState, type FormEvent } from 'react'
import { useIssue } from '../hooks/useIssue'
import { useUpdateStatus } from '../hooks/useUpdateStatus'
import { useUpdateIssue } from '../hooks/useUpdateIssue'
import { StatusBadge } from '../components/StatusBadge'
import { IssueForm, type IssueFormValue } from '../components/IssueForm'
import type { Status } from '../types/issue'
import { CATEGORY_LABELS, PRIORITY_LABELS } from '../types/issue'
import { deleteIssue, photoPath } from '../api/issues'
import { http } from '../api/client'
import { getStoredUser, isAdmin } from '../api/auth'

interface Props {
  id: string
  onBack: () => void
}

const EMPTY_DRAFT: IssueFormValue = {
  title: '',
  description: '',
  category: 'other',
  priority: 'medium',
  reporterName: '',
}

export function IssueDetailPage({ id, onBack }: Props) {
  const { issue, loading, error, refresh } = useIssue(id)
  const { change, pending, error: updateError } = useUpdateStatus()
  const { save, pending: saving, error: saveError, clearError } = useUpdateIssue()

  // 编辑模式开关 + 表单草稿；进入编辑时以当前 issue 值填充。
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<IssueFormValue>(EMPTY_DRAFT)
  // B6 现场照片：存在则取 blob 展示（404 = 无照片，静默不显示）。
  const [photoUrl, setPhotoUrl] = useState<string | null>(null)
  // A4 删除。
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  useEffect(() => {
    let revoked: string | null = null
    let cancelled = false
    if (issue) {
      void (async () => {
        try {
          const res = await http.raw(photoPath(id))
          if (res.status === 404) return // 无照片：正常情况，不报错
          if (!res.ok) return
          const blob = await res.blob()
          if (cancelled) return
          revoked = URL.createObjectURL(blob)
          setPhotoUrl(revoked)
        } catch {
          // 取图失败不影响详情主流程（照片是增强信息）。
        }
      })()
    }
    return () => {
      cancelled = true
      if (revoked) URL.revokeObjectURL(revoked)
      setPhotoUrl(null)
    }
  }, [id, issue])

  useEffect(() => {
    if (issue) {
      setDraft({
        title: issue.title,
        description: issue.description,
        category: issue.category,
        priority: issue.priority,
        reporterName: issue.reporterName ?? '',
      })
    }
  }, [issue])

  const handleStatus = async (to: Status) => {
    const result = await change(id, to)
    // 成功或冲突都刷新详情（冲突时以后端带回的最新状态为准）。
    if (result.issue || result.conflict) {
      await refresh()
    }
  }

  const enterEdit = () => {
    clearError()
    setEditing(true)
  }

  const cancelEdit = () => {
    clearError()
    setEditing(false)
    // 还原草稿为当前值，避免下次进入残留。
    if (issue) {
      setDraft({
        title: issue.title,
        description: issue.description,
        category: issue.category,
        priority: issue.priority,
        reporterName: issue.reporterName ?? '',
      })
    }
  }

  const handleSave = async (e: FormEvent) => {
    e.preventDefault()
    const updated = await save(id, draft)
    if (updated) {
      setEditing(false)
      await refresh()
    }
    // 失败留在表单内，错误信息由 saveError 透出，用户可修正后重试。
  }

  // A4 删除（admin）：二次确认后删除并返回列表。
  const handleDelete = async () => {
    if (!window.confirm('确定删除该问题？删除后不可恢复。')) return
    setDeleting(true)
    setDeleteError(null)
    try {
      await deleteIssue(id)
      onBack()
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : '删除失败')
    } finally {
      setDeleting(false)
    }
  }

  if (loading) return <p className="loading">加载中…</p>
  if (error) {
    // 错误态必须保留导航与重试入口（FR-W-04：错误文案 + 重试入口）。
    return (
      <section className="card">
        <header className="page-header">
          <button onClick={onBack}>← 返回列表</button>
          <h1>问题详情</h1>
        </header>
        <p className="error">加载失败：{error}</p>
        <div className="status-actions">
          <button onClick={() => void refresh()}>重试</button>
        </div>
      </section>
    )
  }
  if (!issue) return <p className="empty">问题不存在</p>

  return (
    <section className="card">
      <header className="page-header">
        <button onClick={onBack}>← 返回列表</button>
        <h1>{issue.title}</h1>
        <StatusBadge status={issue.status} />
      </header>

      {editing ? (
        <IssueForm
          value={draft}
          onChange={setDraft}
          onSubmit={(e) => void handleSave(e)}
          onCancel={cancelEdit}
          pending={saving}
          error={saveError}
        />
      ) : (
        <>
          <dl className="detail">
            <dt>描述</dt>
            <dd>{issue.description || '—'}</dd>
            {photoUrl && (
              <>
                <dt>现场照片</dt>
                <dd>
                  <img
                    className="issue-photo"
                    src={photoUrl}
                    alt="现场照片"
                    onClick={() => window.open(photoUrl)}
                  />
                  <span className="photo-hint">点击放大</span>
                </dd>
              </>
            )}
            <dt>分类</dt>
            <dd>{CATEGORY_LABELS[issue.category] ?? issue.category}</dd>
            <dt>优先级</dt>
            <dd>
              <span className={`priority priority-${issue.priority}`}>
                {PRIORITY_LABELS[issue.priority] ?? issue.priority}
              </span>
            </dd>
            <dt>AR 位置</dt>
            <dd>
              ({issue.position.x.toFixed(2)}, {issue.position.y.toFixed(2)},{' '}
              {issue.position.z.toFixed(2)})，平面 {issue.anchor.planeId || '—'}
            </dd>
            <dt>上报人</dt>
            <dd>{issue.reporterName ?? '—'}</dd>
            <dt>上报时间</dt>
            <dd>{new Date(issue.createdAt).toLocaleString()}</dd>
            <dt>更新时间</dt>
            <dd>{new Date(issue.updatedAt).toLocaleString()}</dd>
            <dt>解决时间</dt>
            <dd>
              {issue.resolvedAt ? new Date(issue.resolvedAt).toLocaleString() : '—'}
            </dd>
          </dl>

          <div className="status-actions">
            {/* 三态任意互转合法（含自转/回退），当前状态按钮禁用仅为体验提示。 */}
            {(['open', 'in_progress', 'resolved'] as Status[]).map((to) => (
              <button
                key={to}
                className="btn"
                disabled={pending || issue.status === to}
                onClick={() => void handleStatus(to)}
              >
                {to === 'open' ? '重新打开' : to === 'in_progress' ? '开始处理' : '标记已解决'}
              </button>
            ))}
            <button className="btn btn-primary" onClick={enterEdit}>
              编辑问题
            </button>
            {isAdmin(getStoredUser()) && (
              <button
                className="btn btn-danger"
                onClick={() => void handleDelete()}
                disabled={deleting}
              >
                {deleting ? '删除中…' : '删除问题'}
              </button>
            )}
          </div>
          {deleteError && <p className="error">{deleteError}</p>}
        </>
      )}

      {updateError && <p className="error">{updateError}</p>}
    </section>
  )
}
