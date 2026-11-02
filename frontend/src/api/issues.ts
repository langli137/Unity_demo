// 后端 API 的类型化封装：路径与参数对应 backend router 的最小集，不得擅自扩展。

import type { Issue, ListData, Status } from '../types/issue'
import { http } from './client'

export interface ListIssuesParams {
  status?: Status | ''
  category?: string
  page: number
  pageSize: number
}

export function listIssues(params: ListIssuesParams): Promise<ListData> {
  const q = new URLSearchParams()
  if (params.status) q.set('status', params.status)
  if (params.category) q.set('category', params.category)
  q.set('page', String(params.page))
  q.set('pageSize', String(params.pageSize))
  return http.get<ListData>(`/api/v1/issues?${q.toString()}`)
}

export function getIssue(id: string): Promise<Issue> {
  return http.get<Issue>(`/api/v1/issues/${encodeURIComponent(id)}`)
}

export function updateStatus(id: string, status: Status): Promise<Issue> {
  return http.patch<Issue>(`/api/v1/issues/${encodeURIComponent(id)}/status`, { status })
}

// 创建入参：position 必带（管理端补录用 {0,0,0} 占位即可），其余与后端 createIssueRequest 对齐。
export interface CreateIssueInput {
  title: string
  description: string
  category: string
  priority: string
  reporterName: string
  position: { x: number; y: number; z: number }
}

// v1.2 管理端创建（POST /api/v1/issues 为既有接口，本条只是前端封装）。
export function createIssue(input: CreateIssueInput): Promise<Issue> {
  return http.post<Issue>('/api/v1/issues', input)
}

// 字段编辑入参：与后端 updateIssueRequest 对齐，仅含可编辑字段。
export interface UpdateIssueInput {
  title: string
  description: string
  category: string
  priority: string
  reporterName: string
}

// v1.2 字段编辑：PATCH /api/v1/issues/{id}（status/position 不在此接口范围）。
export function updateIssue(id: string, input: UpdateIssueInput): Promise<Issue> {
  return http.patch<Issue>(`/api/v1/issues/${encodeURIComponent(id)}`, input)
}

// A4 删除：DELETE /api/v1/issues/{id}（后端仅 admin 放行，前端再按角色隐藏入口）。
export function deleteIssue(id: string): Promise<void> {
  return http.del<void>(`/api/v1/issues/${encodeURIComponent(id)}`)
}

// B6 照片地址（需带 Authorization，不能直接塞给 <img src>——详情页用 raw() 取 blob）。
export function photoPath(id: string): string {
  return `/api/v1/issues/${encodeURIComponent(id)}/photo`
}
