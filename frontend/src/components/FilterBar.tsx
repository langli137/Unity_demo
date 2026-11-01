// 纯展示组件：筛选栏。选项受控，变更通过回调抛给页面。

import type { Category, Status } from '../types/issue'
import { CATEGORY_LABELS, STATUS_LABELS } from '../types/issue'

const CATEGORIES = Object.keys(CATEGORY_LABELS) as Category[]
const STATUSES = Object.keys(STATUS_LABELS) as Status[]

interface Props {
  status: Status | ''
  category: Category | ''
  onStatusChange: (status: Status | '') => void
  onCategoryChange: (category: Category | '') => void
}

export function FilterBar({ status, category, onStatusChange, onCategoryChange }: Props) {
  return (
    <div className="filter-bar">
      <label>
        状态：
        <select
          value={status}
          onChange={(e) => onStatusChange(e.target.value as Status | '')}
        >
          <option value="">全部</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {STATUS_LABELS[s]}
            </option>
          ))}
        </select>
      </label>
      <label>
        分类：
        <select
          value={category}
          onChange={(e) => onCategoryChange(e.target.value as Category | '')}
        >
          <option value="">全部</option>
          {CATEGORIES.map((c) => (
            <option key={c} value={c}>
              {CATEGORY_LABELS[c]}
            </option>
          ))}
        </select>
      </label>
    </div>
  )
}
