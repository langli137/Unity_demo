// 纯展示组件：问题表格（v1.2 美化）。行点击交给页面跳转，不直接发请求。
// 呈现：序号列、标题+描述摘要双行、分类胶囊、优先级/状态徽标、相对时间+完整时间 title。

import type { Issue } from '../types/issue'
import { CATEGORY_LABELS, PRIORITY_LABELS } from '../types/issue'
import { StatusBadge } from './StatusBadge'

interface Props {
  items: Issue[]
  onRowClick: (id: string) => void
}

// 相对时间：仅今天/昨天用相对表述，更早的直接显示日期，避免长字符串撑列。
function formatWhen(iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const day = 24 * 60 * 60 * 1000
  if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate()) {
    return `今天 ${d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })}`
  }
  const yesterday = new Date(now.getTime() - day)
  if (d.getFullYear() === yesterday.getFullYear() && d.getMonth() === yesterday.getMonth() && d.getDate() === yesterday.getDate()) {
    return `昨天 ${d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })}`
  }
  return d.toLocaleDateString('zh-CN')
}

// 短号：取 id 前 6 位，便于现场沟通指认。
function shortId(id: string): string {
  return id.slice(0, 6).toUpperCase()
}

export function IssueTable({ items, onRowClick }: Props) {
  if (items.length === 0) {
    return <p className="empty">暂无问题记录</p>
  }
  return (
    <table className="issue-table">
      <thead>
        <tr>
          <th className="col-index">#</th>
          <th>标题</th>
          <th className="col-category">分类</th>
          <th className="col-priority">优先级</th>
          <th className="col-status">状态</th>
          <th className="col-reporter">上报人</th>
          <th className="col-time">上报时间</th>
        </tr>
      </thead>
      <tbody>
        {items.map((issue, i) => (
          <tr key={issue.id} onClick={() => onRowClick(issue.id)}>
            <td className="col-index">
              <span className="row-index">{i + 1}</span>
              <span className="short-id" title={issue.id}>{shortId(issue.id)}</span>
            </td>
            <td className="col-title">
              <div className="cell-title">{issue.title}</div>
              {issue.description && <div className="cell-sub">{issue.description}</div>}
            </td>
            <td className="col-category">
              <span className="tag">{CATEGORY_LABELS[issue.category] ?? issue.category}</span>
            </td>
            <td className="col-priority">
              <span className={`priority priority-${issue.priority}`}>
                {PRIORITY_LABELS[issue.priority] ?? issue.priority}
              </span>
            </td>
            <td className="col-status">
              <StatusBadge status={issue.status} />
            </td>
            <td className="col-reporter">{issue.reporterName ?? '—'}</td>
            <td className="col-time">
              <time title={new Date(issue.createdAt).toLocaleString()}>
                {formatWhen(issue.createdAt)}
              </time>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
