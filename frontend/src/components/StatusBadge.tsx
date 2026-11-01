// 纯展示组件：状态徽标。不发请求、不写业务判断。

import type { Status } from '../types/issue'
import { STATUS_LABELS } from '../types/issue'

export function StatusBadge({ status }: { status: Status }) {
  return <span className={`badge badge-${status}`}>{STATUS_LABELS[status]}</span>
}
