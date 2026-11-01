// Issue 等类型：字段与后端契约严格一致（camelCase），与 backend/internal/model/issue.go 的 json tag 一一对应。

export type Category = 'wall_damage' | 'equipment_fault' | 'safety_hazard' | 'other'

export type Priority = 'low' | 'medium' | 'high'

// v1.1 三态契约：任意互转合法（含自转/回退），409 仅并发冲突。
export type Status = 'open' | 'in_progress' | 'resolved'

export interface Vector3 {
  x: number
  y: number
  z: number
}

export interface Anchor {
  rotation: Vector3
  planeId: string
}

export interface Location {
  latitude?: number
  longitude?: number
}

export interface Issue {
  id: string
  title: string
  description: string
  category: Category
  priority: Priority
  status: Status
  position: Vector3
  anchor: Anchor
  location: Location
  reporterName?: string
  createdAt: string
  updatedAt: string
  resolvedAt?: string
}

// 列表接口 data 契约。
export interface ListData {
  items: Issue[]
  total: number
  page: number
  pageSize: number
}

// 统一响应壳。
export interface ApiResponse<T> {
  code: number
  message: string
  data?: T
}

export const CATEGORY_LABELS: Record<Category, string> = {
  wall_damage: '墙面破损',
  equipment_fault: '设备异常',
  safety_hazard: '安全隐患',
  other: '其他',
}

export const PRIORITY_LABELS: Record<Priority, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

export const STATUS_LABELS: Record<Status, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已解决',
}

// 三态全部可作为修改目标（后端任意互转合法），当前状态仅用于徽标与提示。
export const ALL_STATUSES: Status[] = ['open', 'in_progress', 'resolved']
